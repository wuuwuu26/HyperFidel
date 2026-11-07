#!/bin/bash
set -e

case "$1" in
    remove|purge)
        systemctl stop hyperfidel-server 2>/dev/null || true
        systemctl disable hyperfidel-server 2>/dev/null || true
        systemctl disable --now hyperfidel-server-updater.path 2>/dev/null || true
        ;;
esac
