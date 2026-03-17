# Network Access Configuration

## Overview

This document describes how to configure TorrentCore components for network access, allowing the Web UI and Avalonia desktop client to connect from any machine on the local network.

## Architecture

TorrentCore consists of three components with different network requirements:

| Component | Type | Network Role |
|-----------|------|--------------|
| **TorrentCore.Service** | ASP.NET Core API | Server (must bind to network interface) |
| **TorrentCore.Web** | Blazor Server UI | Server (must bind to network interface) |
| **TorrentCore.Avalonia** | Desktop App | Client (connects to Service API) |

## Network Binding Concepts

### Localhost vs Network Binding

- **`localhost` / `127.0.0.1`**: Only accessible from the same machine
- **`0.0.0.0`**: Binds to all network interfaces, allowing remote access
- **Specific IP (e.g., `192.168.68.80`)**: Binds to a specific network interface

**For network access, use `0.0.0.0` in URL bindings.**

### Port Selection

Default ports used by TorrentCore:

| Component | HTTP Port | HTTPS Port |
|-----------|-----------|------------|
| Service | 5078 | 7033 |
| Web | 5131 | 7053 |

These ports must be open in the host machine's firewall for remote access.

## Configuration by Component

### TorrentCore.Service (API Backend)

The Service hosts the REST API and MonoTorrent engine. It must be accessible from:
- The Web UI (server-to-server)
- Avalonia clients (client-to-server)
- Any other integration clients (e.g., TVMaze)

#### Development Configuration

Edit `src/TorrentCore.ServiceHost/Properties/launchSettings.json`:

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://0.0.0.0:5078",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "https://0.0.0.0:7033;http://0.0.0.0:5078",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

#### Production Configuration

**Option 1: appsettings.json**

Add to `src/TorrentCore.ServiceHost/appsettings.json`:

```json
{
  "Urls": "https://0.0.0.0:7033;http://0.0.0.0:5078",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

**Option 2: Environment Variable**

```bash
export ASPNETCORE_URLS="https://0.0.0.0:7033;http://0.0.0.0:5078"
dotnet TorrentCore.Service.dll
```

**Option 3: Command-Line Argument**

```bash
dotnet TorrentCore.Service.dll --urls "https://0.0.0.0:7033;http://0.0.0.0:5078"
```

### TorrentCore.Web (Remote Web UI)

The Web UI is a Blazor Server application that must be accessible from:
- User browsers on the network
- Internally, it makes server-side HTTP calls to TorrentCore.Service

#### Development Configuration

Edit `src/TorrentCore.Web/Properties/launchSettings.json`:

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://0.0.0.0:5131",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://0.0.0.0:7053;http://0.0.0.0:5131",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

#### Production Configuration

Edit `src/TorrentCore.Web/appsettings.json`:

```json
{
  "Urls": "https://0.0.0.0:7053;http://0.0.0.0:5131",
  "TorrentCoreService": {
    "BaseUrl": "https://192.168.68.80:7033/"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

**Important:** The `TorrentCoreService.BaseUrl` must point to the network-accessible address of the Service host.

### TorrentCore.Avalonia (Desktop Client)

Avalonia is a desktop application that only makes outbound HTTP requests. It does not bind to any ports or require network access for itself.

#### Configuration

Edit `src/TorrentCore.Avalonia/Config/appsettings.json`:

```json
{
  "TorrentCoreService": {
    "BaseUrl": "https://192.168.68.80:7033/"
  }
}
```

**Note:** Replace `192.168.68.80` with the actual IP address or hostname of the machine running TorrentCore.Service.

## Network Topology Examples

### Single-Host Deployment

All components on one machine, accessed from network clients:

```
Machine: 192.168.68.80
├── TorrentCore.Service  (binds to 0.0.0.0:7033)
└── TorrentCore.Web      (binds to 0.0.0.0:7053, calls http://localhost:7033)

Remote Client Machine A:
└── Browser → https://192.168.68.80:7053 (Web UI)

Remote Client Machine B:
└── Avalonia → https://192.168.68.80:7033 (Service API)
```

**Web UI Config:**
```json
{
  "TorrentCoreService": {
    "BaseUrl": "https://localhost:7033/"  // Same machine
  }
}
```

### Multi-Host Deployment

Service on a dedicated server, Web UI on another:

```
Server Machine: 192.168.68.80
└── TorrentCore.Service  (binds to 0.0.0.0:7033)

Web Host Machine: 192.168.68.81
└── TorrentCore.Web      (binds to 0.0.0.0:7053, calls Service at 192.168.68.80:7033)

Remote Client Machine:
└── Browser → https://192.168.68.81:7053 (Web UI)
└── Avalonia → https://192.168.68.80:7033 (Service API)
```

**Web UI Config:**
```json
{
  "TorrentCoreService": {
    "BaseUrl": "https://192.168.68.80:7033/"  // Remote Service host
  }
}
```

## Security Considerations

### HTTPS Certificates

#### Development

ASP.NET Core uses a self-signed development certificate by default:

```bash
dotnet dev-certs https --trust
```

**Remote Access Issue:** Browsers on other machines will show certificate warnings because the cert is only trusted on the development machine.

**Workarounds:**
1. Accept the browser warning (not recommended for production)
2. Export and import the dev cert on client machines
3. Use HTTP in development (not recommended)
4. Set up a proper certificate

#### Production

**Option 1: Reverse Proxy with Let's Encrypt**

Use nginx or Caddy with automatic HTTPS:

```nginx
# nginx example
server {
    listen 443 ssl http2;
    server_name torrent.example.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://localhost:7033;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```

**Option 2: Kestrel with Certificate**

Configure Kestrel directly in `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://0.0.0.0:7033",
        "Certificate": {
          "Path": "/path/to/certificate.pfx",
          "Password": "cert-password"
        }
      }
    }
  }
}
```

### Firewall Configuration

Ensure the following ports are open on the host machine:

**macOS:**
```bash
# Check firewall status
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate

# Add application to firewall (if enabled)
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --add /path/to/TorrentCore.Service
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --add /path/to/TorrentCore.Web
```

**Linux (ufw):**
```bash
sudo ufw allow 7033/tcp  # Service HTTPS
sudo ufw allow 7053/tcp  # Web HTTPS
sudo ufw allow 5078/tcp  # Service HTTP (optional)
sudo ufw allow 5131/tcp  # Web HTTP (optional)
```

**Linux (firewalld):**
```bash
sudo firewall-cmd --permanent --add-port=7033/tcp
sudo firewall-cmd --permanent --add-port=7053/tcp
sudo firewall-cmd --reload
```

### Authentication and Authorization

**Current State:** TorrentCore v1 does not implement authentication.

**Security Implications:**
- Anyone with network access can manage torrents
- Suitable for trusted local networks only
- Not suitable for internet-facing deployments without additional security layers

**Recommended for Production:**
1. Place behind a reverse proxy with authentication (nginx + basic auth, OAuth)
2. Use VPN for remote access
3. Implement IP allowlisting
4. Plan for authentication in v2 (API keys, JWT, etc.)

## Troubleshooting

### Cannot Connect from Remote Machine

**Symptoms:** Web UI or Avalonia client cannot reach the Service.

**Checklist:**
1. Verify Service is binding to `0.0.0.0`, not `localhost`:
   ```bash
   netstat -an | grep 7033
   # Should show: tcp4       0      0  *.7033                 *.*                    LISTEN
   # NOT:         tcp4       0      0  127.0.0.1.7033         *.*                    LISTEN
   ```

2. Verify firewall allows the ports:
   ```bash
   # Test from remote machine
   telnet 192.168.68.80 7033
   ```

3. Check Service is actually running:
   ```bash
   curl -k https://192.168.68.80:7033/api/health
   ```

4. Verify `BaseUrl` in client configs points to the correct IP/hostname

### HTTPS Certificate Warnings

**Symptoms:** Browser shows "Your connection is not private" or similar.

**Cause:** Self-signed development certificate not trusted on client machine.

**Solutions:**
1. Development: Accept the warning temporarily
2. Development: Export dev cert and import on client machines
3. Production: Use a proper certificate (Let's Encrypt, etc.)
4. Last resort: Use HTTP in development (update `BaseUrl` to `http://...`)

### Blazor SignalR Connection Failures

**Symptoms:** Web UI loads but shows connection errors.

**Cause:** Blazor Server requires WebSocket support.

**Solutions:**
1. Ensure reverse proxy (if used) supports WebSocket upgrades
2. Check browser console for specific connection errors
3. Verify `AllowedHosts` is set to `*` or includes the actual hostname

### Avalonia Cannot Connect

**Symptoms:** Avalonia app shows connection errors.

**Checklist:**
1. Verify `appsettings.json` `BaseUrl` matches the Service host
2. Check HTTPS certificate is valid (or use HTTP for testing)
3. Ensure Service API is accessible:
   ```bash
   curl -k https://192.168.68.80:7033/api/health
   ```
4. Check Avalonia app has network permissions (macOS: System Preferences → Security)

## Quick Start Checklist

### For Service Host Setup:

- [ ] Update `launchSettings.json` or `appsettings.json` with `0.0.0.0` bindings
- [ ] Open firewall ports 7033 and 5078
- [ ] Note the machine's IP address (e.g., `192.168.68.80`)
- [ ] Start the Service and verify it's accessible: `curl -k https://[IP]:7033/api/health`

### For Web UI Setup:

- [ ] Update `launchSettings.json` or `appsettings.json` with `0.0.0.0` bindings
- [ ] Update `TorrentCoreService.BaseUrl` to point to the Service host IP
- [ ] Open firewall ports 7053 and 5131
- [ ] Start the Web UI and access from a browser: `https://[IP]:7053`

### For Avalonia Client Setup:

- [ ] Update `appsettings.json` `BaseUrl` to point to the Service host IP
- [ ] Build and run the Avalonia app
- [ ] Verify connection to Service API

## Summary

**Key Configuration Points:**

1. **Service**: Bind to `0.0.0.0:7033` to allow network access
2. **Web**: Bind to `0.0.0.0:7053` and configure `BaseUrl` to point to Service
3. **Avalonia**: Configure `BaseUrl` to point to Service (no binding needed)
4. **Firewall**: Open ports 7033 (Service) and 7053 (Web)
5. **HTTPS**: Use proper certificates in production; accept warnings in development
6. **Security**: Implement authentication/authorization before internet-facing deployment
