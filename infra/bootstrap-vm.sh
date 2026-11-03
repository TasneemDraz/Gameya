#!/usr/bin/env bash
# One-time VM setup. Run as root on a fresh Ubuntu 24.04 (aarch64) Oracle VM; safe to re-run.
#   sudo ./bootstrap-vm.sh "<ssh public key for the deploy user>"
# Run it from a copy of the repo's infra/ directory (it installs the backup units that sit next to it).
set -euo pipefail

PUBKEY="${1:?usage: bootstrap-vm.sh \"<ssh public key>\"}"
[[ "$(id -u)" -eq 0 ]] || { echo "run as root (sudo)" >&2; exit 1; }
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DEBIAN_FRONTEND=noninteractive

echo "== packages"
apt-get update -qq
apt-get install -y -qq ca-certificates curl gnupg rclone unattended-upgrades iptables iptables-persistent rsync

echo "== docker engine"
if ! command -v docker > /dev/null || ! docker compose version > /dev/null 2>&1; then
  install -m 0755 -d /etc/apt/keyrings
  if [[ ! -f /etc/apt/keyrings/docker.asc ]]; then
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
  fi
  # shellcheck disable=SC1091
  . /etc/os-release
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -qq
  apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
systemctl enable --now docker

echo "== unattended upgrades"
cat > /etc/apt/apt.conf.d/20auto-upgrades << 'CONF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
CONF

echo "== deploy user"
if ! id deploy > /dev/null 2>&1; then
  useradd --create-home --shell /bin/bash deploy
fi
usermod -aG docker deploy
install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
touch /home/deploy/.ssh/authorized_keys
if ! grep -qxF "$PUBKEY" /home/deploy/.ssh/authorized_keys; then
  echo "$PUBKEY" >> /home/deploy/.ssh/authorized_keys
fi
chown deploy:deploy /home/deploy/.ssh/authorized_keys
chmod 600 /home/deploy/.ssh/authorized_keys

echo "== sshd hardening"
# 10- sorts before cloud-init's 50-cloud-init.conf, and sshd uses the first value it sees.
cat > /etc/ssh/sshd_config.d/10-gameya.conf << 'CONF'
PasswordAuthentication no
PermitRootLogin no
CONF
sshd -t
systemctl reload ssh

echo "== firewall (80/443)"
# Oracle's Ubuntu images end INPUT with a REJECT rule, so ACCEPT rules must come first.
for port in 80 443; do
  iptables -C INPUT -p tcp --dport "$port" -j ACCEPT 2> /dev/null \
    || iptables -I INPUT 1 -p tcp --dport "$port" -j ACCEPT
done
netfilter-persistent save

echo "== /opt/gameya"
install -d -o deploy -g deploy /opt/gameya /opt/gameya/caddy /opt/gameya/releases /opt/gameya/backups

echo "== backup timer"
install -m 0644 "$HERE/gameya-backup.service" /etc/systemd/system/gameya-backup.service
install -m 0644 "$HERE/gameya-backup.timer" /etc/systemd/system/gameya-backup.timer
systemctl daemon-reload
systemctl enable --now gameya-backup.timer

echo "bootstrap complete. Log in as deploy with the key you passed; password login and root login are now disabled."
