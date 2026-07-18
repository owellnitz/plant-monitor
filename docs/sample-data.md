# Publishing sample data over MQTT

No sensor hardware needed — any MQTT client can stand in for the firmware.
Requires the server stack running (`docker compose up -d`) and an MQTT client
such as `mosquitto_pub` (`brew install mosquitto`).

## Message format

The firmware publishes one JSON message per reading to
`sensors/<device_id>/moisture`:

```json
{"id":"<device_id>","raw":<adc_value>,"percent":<moisture_percent>}
```

`raw` is the ADC value — drier soil reads higher. `percent` is the derived
moisture level. A plausible mapping for fake data: `raw = 1200 + (100 - percent) * 28`.

## Publish a single reading

```sh
mosquitto_pub -h localhost -t 'sensors/sensor-001/moisture' \
  -m '{"id":"sensor-001","raw":2376,"percent":58}'
```

The device appears in the UI as an unassigned sensor with one reading;
assign it to a plant to test the binding flow.

## Publish a series of readings

To fill the chart for a device, publish several values in a row:

```sh
for p in 71 64 58 52 47 44 51 57 62 59 55 58; do
  mosquitto_pub -h localhost -t 'sensors/sensor-001/moisture' \
    -m "{\"id\":\"sensor-001\",\"raw\":$((1200 + (100 - p) * 28)),\"percent\":$p}"
  sleep 0.3
done
```

Repeat with different device ids (`sensor-002`, …) to get multiple sensors.

## Caveats

- **No plants are created.** MQTT only carries readings, so every device shows
  up as an unassigned sensor; bind it to a plant in the UI.
- **No real history.** The backend timestamps readings on receipt, so points
  cluster around the time you publish rather than spanning days.
