# Inactive Relay Feature

The Inactive Relay feature provides a "safety" mechanism that automatically activates a designated relay when the RemoteRelay server has no active client connections or is shutting down. This is useful for fail-safe scenarios in critical broadcast or audio routing applications.

## Purpose

In radio broadcasting and professional audio environments, it's important to have a backup state when the control system becomes unavailable. The inactive relay can be configured to:

- Switch to a backup audio source when the server is offline
- Activate a warning indicator when no clients are connected
- Enable an emergency bypass circuit
- Trigger any other safety mechanism in your system

## Configuration

The inactive relay is configured in the server's `config.json` file:

```json
"InactiveRelay": {
  "Pin": 25,
  "InactiveState": "High"
}
```

### Properties

#### Pin (Required)

The GPIO pin number (BCM numbering) that controls the inactive relay.

```json
"Pin": 25
```

- Must be a valid GPIO pin on your Raspberry Pi
- Should not conflict with other relay pins defined in `Routes` or `PhysicalSourceButtons`
- Common choices: GPIO pins that aren't used for primary relay control

#### InactiveState (Required)

The state the pin should be set to when the system is "inactive" (no clients connected or server shutting down).

```json
"InactiveState": "High"
```

**Options:**
- `"High"`: Set pin HIGH (3.3V) when inactive
- `"Low"`: Set pin LOW (0V) when inactive

Choose the state based on how your relay hardware is wired and what you want to activate during inactive periods.

### Disabling the Feature

To disable the inactive relay feature entirely, set the value to `null`:

```json
"InactiveRelay": null
```

## Behavior

### When the Inactive Relay Activates

The inactive relay pin is set to the configured `InactiveState` in the following scenarios:

1. **Server Startup** - Before any clients connect
2. **All Clients Disconnect** - When the last client disconnects from the server
3. **Server Shutdown** - When the server is gracefully shut down

### When the Inactive Relay Deactivates

The inactive relay pin is set to the opposite of `InactiveState` when:

1. **First Client Connects** - As soon as any client establishes a connection
2. **Server Fully Running** - During normal operation with connected clients

### State Transitions

```
Server Starts → Inactive Relay Activates (InactiveState)
                        ↓
First Client Connects → Inactive Relay Deactivates (opposite of InactiveState)
                        ↓
Normal Operation → Inactive Relay remains deactivated
                        ↓
Last Client Disconnects → Inactive Relay Activates (InactiveState)
                        ↓
Server Stops → Inactive Relay set to InactiveState
```

## Example Use Cases

### Backup Audio Source in Radio Broadcasting

**Scenario:** Automatically switch to automation system when control clients are offline.

**Configuration:**
```json
"InactiveRelay": {
  "Pin": 25,
  "InactiveState": "High"
}
```

**Wiring:**
- GPIO 25 connected to relay that switches audio to backup automation computer
- When HIGH (inactive): Automation is routed to transmitter
- When LOW (active): Studio routing controlled normally by RemoteRelay

This ensures uninterrupted broadcast even if the RemoteRelay server crashes or loses all client connections.

### Emergency Warning Light

**Scenario:** Illuminate a warning light when the control system is not fully operational.

**Configuration:**
```json
"InactiveRelay": {
  "Pin": 25,
  "InactiveState": "High"
}
```

**Wiring:**
- GPIO 25 connected to relay that powers a red warning light
- When HIGH (inactive): Warning light is ON
- When LOW (active): Warning light is OFF

Operators know immediately if the system is not under normal control.

### Backup Power to Critical Equipment

**Scenario:** Enable backup power supply when control system is offline.

**Configuration:**
```json
"InactiveRelay": {
  "Pin": 25,
  "InactiveState": "Low"
}
```

**Wiring:**
- GPIO 25 connected to relay controlling backup power circuit
- When LOW (inactive): Backup power enabled
- When HIGH (active): Normal power operation

## Hardware Considerations

### Relay Type

- Most relay HATs use active-low logic (relay activates when pin is LOW)
- Check your specific relay module's datasheet
- The `InactiveState` setting should match the desired physical relay state for your safety scenario

### Pin Selection

- Choose a GPIO pin that is not used for primary relay control (`Routes`) or physical buttons (`PhysicalSourceButtons`)
- Avoid GPIO pins with special functions on boot (GPIO 2, 3)
- Common safe choices: GPIO 25, 26, 27 (if not already in use)

### Current Requirements

- GPIO pins provide 3.3V at low current (~16mA max)
- Always use appropriate relay driver circuits
- Never connect high-power loads directly to GPIO pins

## Monitoring

The server logs inactive relay state changes:

```
[INFO] Inactive relay activated - no clients connected
[INFO] Inactive relay deactivated - client connected
```

Monitor these logs to track when your system enters/exits inactive mode.

## Testing

To test the inactive relay feature:

1. **Start the server** - Verify inactive relay activates
2. **Connect a client** - Verify inactive relay deactivates  
3. **Disconnect all clients** - Verify inactive relay activates again
4. **Stop the server** - Verify inactive relay is set to inactive state

Use a multimeter or LED indicator to physically verify the GPIO pin state changes.

## Troubleshooting

### Inactive Relay Not Activating

**Check:**
- `InactiveRelay` is not set to `null` in configuration
- Pin number is valid and not conflicting with other uses
- Relay hardware is properly connected to the specified GPIO pin
- Check server logs for initialization errors

### Relay Activating at Wrong Times

**Verify:**
- `InactiveState` setting matches your hardware expectations
- Client connections are stable (check for frequent connect/disconnect cycles)
- No GPIO conflicts with other relay pins

### Physical Relay Not Switching

**Possible causes:**
- Relay module requires opposite logic level - try changing `InactiveState`
- Hardware fault in relay module
- Insufficient power supply to relay module
- Incorrect wiring between GPIO and relay module
