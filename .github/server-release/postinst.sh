#!/bin/bash
set -e

# Create system group and user for the service
if ! getent group hyperfidel-server > /dev/null 2>&1; then
    groupadd --system hyperfidel-server
fi
if ! id -u hyperfidel-server > /dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin \
        --gid hyperfidel-server hyperfidel-server
fi

# Ensure correct permissions
chmod 755 /usr/lib/hyperfidel-server/HyperFidel.Server
chown -R root:root /usr/lib/hyperfidel-server
chown -R hyperfidel-server:hyperfidel-server /etc/hyperfidel-server

# Data directory for the service (SQLite database, caches, downloaded artwork).
# The service user has no writable home, so the server is pointed here via
# HYPERFIDEL_DATA_DIR in the systemd unit; create it owned by the service user.
install -d -o hyperfidel-server -g hyperfidel-server /var/lib/hyperfidel-server

# Register the systemd unit (do not auto-start — user must configure first)
systemctl daemon-reload
systemctl enable --now hyperfidel-server-updater.path
