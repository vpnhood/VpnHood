# Contributing to VpnHood

Thank you for helping. Bug reports, ideas and fixes are all welcome.

## Before you start

- **A question or an idea:** start a [discussion](https://github.com/vpnhood/VpnHood/discussions).
- **A bug:** open an [issue](https://github.com/vpnhood/VpnHood/issues) with the version of the app or the server,
  the platform, and the steps that show the problem.
- **A security problem:** never in public; [SECURITY.md](SECURITY.md) says how to report it.
- **A larger change:** open an issue first, so we agree on the approach before you write the code.

## Pull requests

1. Fork the repository and branch from `develop`; pull requests go to `develop`.
2. Build as [Build from source](README.md#build-from-source) shows, and run the tests of what you changed, in
   [`tests`](tests).
3. Follow the rules in [docs/implementation-rules.md](docs/implementation-rules.md) and the code style the repository
   sets in `.editorconfig` and `VpnHood.sln.DotSettings`.
4. Keep a pull request to one change, and say in its description what it changes and why.
5. A change users notice goes in its changelog: [CHANGELOG.md](CHANGELOG.md) for the apps,
   [CHANGELOG.Server.md](CHANGELOG.Server.md) for the server, [CHANGELOG.Packages.md](CHANGELOG.Packages.md) for the
   NuGet packages.

## License

What you contribute is licensed under the [GNU Lesser General Public License 2.1](LICENSE), as the rest of the
repository.
