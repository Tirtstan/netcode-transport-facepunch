# Facepunch Transport for Netcode for GameObjects

By **Nico Thomas** and **Floris van Onna**. Credits to **Garry Newman**, author of Facepunch.Steamworks.

This is a fork of the [Unity community Facepunch transport](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/main/Transports/com.community.netcode.transport.facepunch). We use it in our game [GET RAMMED](https://store.steampowered.com/app/4612760/GET_RAMMED/) with Netcode for GameObjects 2.13.

## Install

In the Package Manager, choose **Add package from git URL** and enter:

```
https://github.com/Tirtstan/netcode-transport-facepunch.git
```

The package name matches upstream, so it replaces the upstream package directly.

This package does not include Facepunch.Steamworks. Add [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks/releases) to your project yourself. We use version 2.4.1.

## Steam setup

Your game starts Steam, calls `SteamClient.RunCallbacks()` every frame, and shuts Steam down. The transport does none of this. If Steam isn't running when you start a client or server, the transport calls `SteamClient.Init` with the Steam App ID from the Inspector. If Steam or the relay network isn't available, `StartClient` and `StartServer` return `false`.

## Changes from upstream

See [CHANGELOG.md](CHANGELOG.md).
