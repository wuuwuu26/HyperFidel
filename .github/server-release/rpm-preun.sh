#!/bin/bash
# $1 = 0 means uninstall; $1 = 1 means upgrade
if [ "$1" -eq 0 ]; then
    systemctl stop hyperfidel-server 2>/dev/null || true
    systemctl disable hyperfidel-server 2>/dev/null || true
    systemctl disable --now hyperfidel-server-updater.path 2>/dev/null || true
fi
