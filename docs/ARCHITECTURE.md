# PULSE architecture

```mermaid
flowchart TB
    Browser["Browser<br/>Participant cookie · Chart.js"]
    subgraph App["One ASP.NET Core process · .NET 10"]
        UI["Blazor Interactive Server<br/>Questions · Timeline · Room"]
        Writes["Command services<br/>Identity / role checks · Validation"]
        Store["EventStore · EF Core<br/>SQLite write transaction<br/>Request deduplication · Conflict checks<br/>Sequence + PreviousHash + Hash"]
        Reads["Read services<br/>Question / evidence visibility<br/>Room state check every 2 seconds per viewer"]
        Replay["Pure replay / ResultCalculator<br/>Latest vote per actor at a sequence<br/>v1 / v2 calculations"]
        Present["ResultService<br/>Prepare or reuse output<br/>Admin: compare stored output with replay"]
        Verify["EventChainVerifier<br/>Admin: verify the global chain"]
    end
    subgraph DB["One SQLite database · App_Data/pulse.db"]
        Events[("Events<br/>Authoritative append-only history")]
        Results[("PresentedResults<br/>Output + boundary + version<br/>First-prepared time")]
    end
    Browser <-->|"HTTP / Blazor SignalR"| UI
    UI --> Writes
    Writes --> Store
    Store -->|"Read current state; append atomically"| Events
    UI --> Reads
    Events --> Reads
    Reads -->|"Filtered history / chart"| Replay
    Replay -->|"Timeline / chart data"| UI
    UI --> Present
    Events -->|"Question history"| Present
    Present --> Replay
    Present <-->|"Store / reuse by question + boundary + version"| Results
    Present -->|"Current result / comparison"| UI
    UI --> Verify
    Events --> Verify
    Verify -->|"Verification report"| UI
```

Arrows show calls or data flow, not separate network services. Room polling runs
on the server; SignalR transports UI changes. Timeline/chart reconstruction creates
no additional presentation records. Append-only rules are application guarantees;
the database owner can bypass them. See [DECISIONS.md](../DECISIONS.md) for limits.
