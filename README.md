# PULSE

A polling app that keeps earlier votes, records evidence and shows how a group's
views change. Built with .NET 10, Blazor Interactive Server, EF Core, SQLite and Chart.js.

## Run locally

Requires the .NET 10 SDK. From the repository root:

```sh
dotnet restore Pulse.slnx
dotnet tool install --global dotnet-ef --version 10.0.12
dotnet ef database update --project src/Pulse.Web
dotnet run --project src/Pulse.Web --launch-profile http
```

If `dotnet-ef` 10.0.12 is already installed, skip the install command; if an older
version is installed, use `dotnet tool update --global dotnet-ef --version 10.0.12`.

Open [http://localhost:5212](http://localhost:5212). The HTTP profile runs in
Development, so no HTTPS certificate setup is needed. Initial setup requires
NuGet access; no database server, Node.js or API keys are required.

The database is `src/Pulse.Web/App_Data/pulse.db` and survives restarts. It starts
empty; migrations must be applied before using the app. Local database files are
excluded from Git.

## Try it

1. Create a question, copy its link, vote and revise your answer or confidence.
2. Open that link in a different browser or private session for another participant.
3. Submit evidence. Use a different participant identity, switch to **Moderator**
   and review it. Switching roles keeps the identity, so you cannot review your own evidence.
4. Switch to **Administrator**, select **Feature in Room**, then open `/room` in
   another tab. Vote on the question page to see the Room update within roughly two seconds.
5. As Administrator, use chain verification and **Stored results & replay** to
   inspect integrity and compare recorded calculations.

The role switcher is a local demo feature enabled by `Demo:EnableRoleSwitching`
in `appsettings.Development.json`; it is unavailable outside Development. There
is no production account or staff-provisioning system. Reload other open tabs
after changing roles.

## Tests and design

```sh
dotnet test Pulse.slnx
```

The focused tests use disposable SQLite databases, not your application data.
See [test scope](tests/README.md), [decisions and limitations](DECISIONS.md) and
the [architecture diagram](docs/ARCHITECTURE.md).
