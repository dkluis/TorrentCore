# TorrentCore

TorrentCore is a standalone torrent engine product built with C# 14 and .NET 10.

TVMaze is a client and integration surface. It does not own TorrentCore internals.

The new desktop browser UI runs alongside the existing WebUI at port **7510**. It uses WebAssembly, MudBlazor, and Kestrel, with Native macOS workflows. Connection profiles remain separate per browser, shared across tabs, and preserved across deployments. See [development](docs/development.md) and [deployment](docs/deployment.md).

Start here:

- [AGENTS.md](AGENTS.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/development.md](docs/development.md)
- [docs/database.md](docs/database.md)
- [docs/operator-settings.md](docs/operator-settings.md)
- [docs/troubleshooting.md](docs/troubleshooting.md)
- [docs/testing.md](docs/testing.md)
- [docs/deployment.md](docs/deployment.md)
- [docs/native-apple-client-development-plan.md](docs/native-apple-client-development-plan.md)

Historical planning and handoff material lives under [docs/archive/](docs/archive/).
