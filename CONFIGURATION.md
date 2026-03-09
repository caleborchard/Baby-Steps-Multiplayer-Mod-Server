# Configuration
## File
You can use settings.cfg to change default server settings. <br>
Default settings.cfg is located near the executable and looks like this:
```
port=7777
password=
player_transmit_cutoff=10
outer_player_transmit_cutoff=500
static_update_rate=1000
max_bandwidth_kbps=512
telemetry_enabled=false
telemetry_update_interval=5000
voice_chat_enabled=true
discord_webhook_url=
discord_webhook_enabled=false
```

## Launch arguments
You can pass individual settings as a launch arguments.<br>
Example: `dotnet BabyStepsMultiplayerServer.dll --static_update_rate=200`<br>
Launch arguments take priority over settings.cfg contents.

# Docker
## How to use this image
### Using docker
Get the docker image by running the following commands:
```
docker pull ghcr.io/azim/baby-steps-server:latest
```
Start a server instance:
```
docker run --name BabyStepsMultiplayerServer -p 7777:7777/udp -d ghcr.io/azim/baby-steps-server:latest
```

### Using docker compose

```yaml
services:
  bsms:
    image: ghcr.io/azim/baby-steps-server:latest
    restart: unless-stopped
    container_name: BabyStepsMultiplayerServer
    ports:
      - 7777:7777/udp
    # volumes:
    #   - "./settings.cfg:/app/settings.cfg"
    # environment:
    #   PORT: "7777"
    #   PASSWORD: ""
    #   PLAYER_TRANSMIT_CUTOFF: "10"
    #   OUTER_PLAYER_TRANSMIT_CUTOFF: "500"
    #   STATIC_UPDATE_RATE: "1000"
    #   MAX_BANDWIDTH_KBPS: "512"
    #   TELEMETRY_ENABLED: "false"
    #   TELEMETRY_UPDATE_INTERVAL: "5000"
    #   VOICE_CHAT_ENABLED: "true"
    #   DISCORD_WEBHOOK_URL: ""
    #   DISCORD_WEBHOOK_ENABLED: "false"
```

## Environment Variables
If set, entrypoint.sh will translate those to their corresponding launch arguments and append to the launch command:
|  Variable                        |  Default  |
|:--------------------------------:|:---------:|
| **PORT**                         |   7777    |
| **PASSWORD**                     |           |
| **PLAYER_TRANSMIT_CUTOFF**       |   10      |
| **OUTER_PLAYER_TRANSMIT_CUTOFF** |   500     |
| **STATIC_UPDATE_RATE**           |   1000    |
| **MAX_BANDWIDTH_KBPS**           |   512     |
| **TELEMETRY_ENABLED**            |   false   |
| **TELEMETRY_UPDATE_INTERVAL**    |   5000    |
| **VOICE_CHAT_ENABLED**           |   true    |
| **DISCORD_WEBHOOK_URL**          |           |
| **DISCORD_WEBHOOK_ENABLED**      |   false   |
