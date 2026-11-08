#!/bin/bash
set -e

# Earlier releases shipped as the HyperFi packages with a hyperfi-server system
# user, /etc/hyperfi-server, /var/lib/hyperfi-server and hyperfi-server.service.
# This installer migrates that installation so an upgrade keeps the configured
# ApiKey, library paths and database instead of starting from an empty server.
LEGACY_USER=hyperfi-server
LEGACY_ETC=/etc/hyperfi-server
LEGACY_DATA=/var/lib/hyperfi-server
NEW_ETC=/etc/hyperfidel-server
NEW_DATA=/var/lib/hyperfidel-server

# Stop the previous unit before its files and ownership change under it. With
# an RPM upgrade $1 is 1 and the previous package is still installed.
if [ -f /lib/systemd/system/hyperfi-server.service ] ||
   [ -f /etc/systemd/system/hyperfi-server.service ]; then
    systemctl disable --now hyperfi-server-updater.path 2>/dev/null || true
    systemctl disable --now hyperfi-server 2>/dev/null || true
fi

# Create system group and user for the service
if ! getent group hyperfidel-server > /dev/null 2>&1; then
    groupadd --system hyperfidel-server
fi
if ! id -u hyperfidel-server > /dev/null 2>&1; then
    useradd --system --no-create-home --shell /sbin/nologin \
        --gid hyperfidel-server hyperfidel-server
fi

# Adopt the previous configuration before installing defaults, so an upgrade
# never replaces a configured ApiKey or library list with the shipped template.
if [ -d "${LEGACY_ETC}" ]; then
    install -d -o hyperfidel-server -g hyperfidel-server "${NEW_ETC}"
    if [ -f "${LEGACY_ETC}/appsettings.json" ] && \
       { [ ! -f "${NEW_ETC}/appsettings.json" ] || \
         cmp -s "${LEGACY_ETC}/appsettings.json" "${NEW_ETC}/appsettings.json"; }; then
        cp -a "${LEGACY_ETC}/appsettings.json" "${NEW_ETC}/appsettings.json"
    fi
    cp -a "${LEGACY_ETC}/." "${NEW_ETC}/" 2>/dev/null || true
fi

# Move the previous database, caches and artwork alongside the new data root.
if [ -d "${LEGACY_DATA}" ]; then
    find "${LEGACY_DATA}" -mindepth 1 -maxdepth 1 \
        -exec mv -n {} "${NEW_DATA}/" \; 2>/dev/null || true
fi

# Ensure correct permissions
chmod 755 /usr/lib/hyperfidel-server/HyperFidel.Server
chown -R root:root /usr/lib/hyperfidel-server
chown -R hyperfidel-server:hyperfidel-server "${NEW_ETC}"

# Data directory for the service (SQLite database, caches, downloaded artwork).
# The service user has no writable home, so the server is pointed here via
# HYPERFIDEL_DATA_DIR in the systemd unit; create it owned by the service user.
install -d -o hyperfidel-server -g hyperfidel-server "${NEW_DATA}"
chown -R hyperfidel-server:hyperfidel-server "${NEW_DATA}"

# Retire the superseded service identity. Only the units and the account move
# here; the previous package still owns its files under /usr/lib/hyperfi-server
# and removes them itself when it is uninstalled.
rm -f /lib/systemd/system/hyperfi-server.service \
      /lib/systemd/system/hyperfi-server-updater.service \
      /lib/systemd/system/hyperfi-server-updater.path \
      /etc/systemd/system/hyperfi-server.service \
      /etc/systemd/system/hyperfi-server-updater.path
if id -u "${LEGACY_USER}" > /dev/null 2>&1; then
    userdel "${LEGACY_USER}" 2>/dev/null || true
fi
if getent group "${LEGACY_USER}" > /dev/null 2>&1; then
    groupdel "${LEGACY_USER}" 2>/dev/null || true
fi
rm -rf "${LEGACY_ETC}"

systemctl daemon-reload
systemctl enable --now hyperfidel-server-updater.path
