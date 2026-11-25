# Facepunch Transport for Netcode for GameObjects

By **Nico Thomas**, **Floris van Onna**<br>
Credits to **Garry Newman** (Author of Facepunch.Steamworks)

A fork of the [Unity community Facepunch transport](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/main/Transports/com.community.netcode.transport.facepunch), maintained alongside the game *Get Rammed* and used in production with Netcode for GameObjects 2.x.

## Install

Package Manager → **Add package from git URL**:

```
https://github.com/Tirtstan/netcode-transport-facepunch.git
```

The package name is unchanged (`com.community.netcode.transport.facepunch`), so it is a drop-in replacement for the upstream package.

### Facepunch.Steamworks is not bundled

Unlike upstream, this package does not ship the Facepunch.Steamworks DLLs or the Steam redistributables. Add [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks/releases) (tested with 2.4.1) to your project yourself, e.g. under `Assets/Plugins`.

## Your game owns the Steam client

The transport no longer runs `SteamClient.RunCallbacks()` or calls `SteamClient.Shutdown()`. Your game is expected to initialise Steam, pump callbacks every frame and shut it down. `StartClient`/`StartServer` call `SteamClient.Init(steamAppId)` only if Steam is not already valid, and return `false` (instead of throwing) when Steam or the relay network is unavailable.

## Differences from upstream

See [CHANGELOG.md](CHANGELOG.md).
