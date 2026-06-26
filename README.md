# NextIntegrations

Shared integration **SDKs** for the Horeca POS ecosystem. Neither app owns this code; the POS heads
(**NextTerminal**, **NextCashier**) consume these libraries as dependencies, so an integration added
once is available to all of them and new integrations appear everywhere at once.

## NextIntegrations.Devices

The device-integration SDK — the single home for **how a POS talks to hardware**.

- **Abstractions** (`NextIntegrations.Devices.Abstractions`) — `DeviceConfig` (role / protocol /
  connection), `IDeviceRegistry`, `IDeviceProbe`.
- **Registry** — `JsonDeviceRegistry`: the device "baza", a file-backed catalog at a **shared** path
  (`LocalApplicationData/Horeca/devices.json`), with one-time migration from the legacy
  `NextCashier/devices.json`. This is what makes the device list common to every head.
- **Probe** — `DeviceProbe`: TCP/serial reachability for the connection monitor.
- **Transport** — `IDeviceTransport` + network (TCP 9100) and serial/USB (virtual COM) transports.
- **EscPos** — pure, deterministic ESC/POS rendering over a neutral `EscPosReceipt` model, plus
  label / drawer-kick / customer-display / test builders.
- `AddNextIntegrationsDevices()` — registers the registry + probe.

Licence gating and each app's own port interfaces (`IReceiptPrinter`, `ICashDrawer`, …) stay in the
app and are composed on top of these building blocks.

### Referencing from an app

```xml
<ProjectReference Include="..\..\..\nextintegrations\src\NextIntegrations.Devices\NextIntegrations.Devices.csproj" />
```

(adjust the relative depth to the referencing project). All Horeca repos sit side by side under the
same parent folder, so this local path resolves for both `NextTerminal` and `NextCashier`.
