# Changelog
All notable changes to this package will be documented in this file. The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)

## 2.1.0

### Added
- `TryGetSteamId(transportClientId, out steamId)` exposes the Steam-authenticated identity of a connection, including while Netcode connection approval is pending.
- `LastDisconnectSummary` records the state and end reason of the most recent Steam disconnect for diagnostics.
- `GetCurrentRtt` reports Steam connection ping for the server and each client.
- `EnsureSteamInitialized()` initialises Steam only when it is not already running.
- Rate-limited warnings when `SendMessage` fails, with counts of failures and full send queues.
- Warning when a client connects to its own Steam ID.

### Changed
- The game owns the Steam client lifecycle: the transport no longer pumps `SteamClient.RunCallbacks()` or calls `SteamClient.Shutdown()`.
- `StartClient` and `StartServer` return `false` with an error when Steam or the relay socket is unavailable, instead of continuing with a null manager.
- Routine disconnects (application end reasons) log at Developer level; unexpected ones log as warnings with the end reason.
- Requires Netcode for GameObjects 2.0.0 or newer and Unity 6.
- Assembly no longer references `Unity.Networking.Transport.NetcodeInterop`.

### Fixed
- Disconnect reasons reach kicked clients: `DisconnectRemoteClient` closes with linger so queued reliable messages are delivered before the connection drops.
- Kicked clients raise `NetworkEvent.Disconnect` on the server. The connection is removed in the Steam disconnect callback rather than when the close is requested, which previously suppressed the event.
- Sending to the server before a connection exists logs a warning instead of throwing.

### Removed
- Bundled Facepunch.Steamworks DLLs and Steam redistributables. Add Facepunch.Steamworks to your project separately.

## 2.0.0

### Changed
- Targets the Netcode for GameObjects 1.0.0 package.
- Renamed namespaces from MLAPI to Netcode.

### Removed
- Removed support for channels.
- No longer send 1 byte of channel information in each message.

## 1.0.0
First version of the Facepunch Transport as a Unity package.
Uses Facepunch.Steamworks version 2.3.2
