# Deployment (mini PC, LAN-only)

Runs the server stack on an x86 Linux mini PC inside the home LAN: Mosquitto,
Postgres, and the prebuilt backend/frontend image from GHCR. No inbound access
from the internet — updates are **pull-based**.

## How updates flow (GitOps-lite)

```
CI on main ── builds & pushes ──► ghcr.io/owellnitz/plant-monitor/backend:latest
                                          │  (public package, no auth)
mini PC:                                  ▼
  watchtower ── polls every 5 min ──► pulls :latest, recreates backend
  plant-monitor.timer ── git pull + compose up ──► applies compose/config changes
```

- **Watchtower** rolls out new images fast (only the `backend` container —
  it runs `--label-enable`, so Postgres/Mosquitto are untouched).
- **systemd timer** keeps git as the source of truth for `deploy/compose.yml`.

## One-time setup

### 1. Static IP

Simplest: add a **DHCP reservation** for the mini PC's MAC on the router. No host
config, survives reinstalls.

Alternative (Ubuntu Server, netplan) — `/etc/netplan/01-static.yaml`:

```yaml
network:
  version: 2
  ethernets:
    eth0:                       # check name with `ip link`
      dhcp4: false
      addresses: [192.168.1.50/24]
      routes:
        - to: default
          via: 192.168.1.1
      nameservers:
        addresses: [192.168.1.1, 1.1.1.1]
```

Apply: `sudo netplan apply`.

### 2. Docker

```sh
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker "$USER"   # log out/in after
```

### 3. Clone + configure

```sh
sudo git clone https://github.com/owellnitz/plant-monitor.git /opt/plant-monitor
sudo chown -R "$USER" /opt/plant-monitor
cd /opt/plant-monitor
cp deploy/.env.example deploy/.env
# edit deploy/.env, set a strong POSTGRES_PASSWORD
```

### 4. Start the stack

```sh
docker compose -f deploy/compose.yml --env-file deploy/.env up -d
```

Check: `curl http://localhost/` serves the PWA. From another LAN device,
`http://<static-ip>/`.

### 5. Enable the git-reconcile timer

```sh
sudo cp deploy/plant-monitor.service deploy/plant-monitor.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now plant-monitor.timer
```

Watch: `systemctl status plant-monitor.timer`, `journalctl -u plant-monitor`.

### 6. Point the firmware at the broker

In `firmware/config.toml`, set the MQTT host to the mini PC's static IP, then
build with `--features net` and flash. (Don't print `config.toml` — it holds the
WiFi password.)

## Operations

```sh
docker compose -f deploy/compose.yml --env-file deploy/.env ps      # status
docker logs watchtower                                              # update polling
docker compose -f deploy/compose.yml --env-file deploy/.env pull    # manual image pull
docker compose -f deploy/compose.yml --env-file deploy/.env logs -f backend
```

Postgres data lives in the `postgres-data` volume — back it up with
`docker compose ... exec db pg_dump -U plantmonitor plantmonitor`.
