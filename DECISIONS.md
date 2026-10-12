# Decisions

## 1. What did you build?

PULSE offers shareable questions, Yes/No/Unsure votes with 0-100 confidence, retained
revisions, moderated evidence and a timeline/chart. One shared Room checks for
changes every two seconds on the server; Blazor's existing SignalR connection
carries UI updates.

## 2. What did you deliberately leave out, and why?

Production accounts, source archiving, push notifications, distributed
infrastructure and load testing are outside this prototype. The role switcher is
Development-only; staff provisioning is unfinished. There is no product LLM:
calculations and explanations are deterministic. These omissions keep attention
on history and explainable results.

## 3. What were your three most important design decisions?

**One application/database:** .NET 10, Blazor Interactive Server, EF Core and SQLite
keep setup small and most logic in C#. Chart.js only draws server-calculated data.

**Authoritative event history:** Votes, evidence and Room changes append events.
One transaction assigns the database sequence and links a global SHA-256 chain.
This simplifies ordering but serializes unrelated questions too. Sequence gaps
alone do not indicate loss.

**Versioned results:** Latest votes count equally. v2 also divides an answer's
latest confidence total by total confidence across Yes/No/Unsure. Zero total means
unavailable. Comparing weighted and ordinary shares reveals confidence differences,
not truth or expertise. Weighting favours confident people; averages hide variation,
and newcomers can move the graph without changed minds. New results use v2; v1
remains replayable.

## 4. What does your data model treat as permanent, and what may change?

Committed events and presented results are permanent through application operations;
EF guards reject edits/deletes. New events change current votes, evidence status and
Room selection. Everyone creates, votes and submits evidence. Participants list their
own questions; staff list all. Only Moderators review evidence, never their own,
even after a role switch. Administrators feature questions and verify history/results;
they cannot review evidence. Expected-sequence checks reject stale review/feature
updates rather than silently overwrite another decision.

The hash chain detects changed content or missing middle events unless recomputed.
It cannot recover content or independently detect tail truncation. A database
administrator can rewrite it; timestamps are server claims. There is no external
anchor or absolute immutability guarantee.

## 5. How do you preserve the history of an opinion?

Changing answer or confidence appends a vote; matching the latest vote is a no-op,
checked inside the write transaction. Returning to an earlier answer is a revision.
Calculation selects each participant's latest vote at a sequence boundary. Request
IDs deduplicate retries; reuse with different content is rejected. A protected
180-day cookie identifies a browser, not a person: clearing cookies, expiry or
another browser can create another participant.

PresentedResults retains output, boundary, algorithm and first-prepared time,
reused per question/boundary/version. Replay uses the original algorithm. This is
neither a delivery log nor proof of viewing.

## 6. How do you relate evidence to opinions held at different times?

Submission and review are separate events. Replay to a vote's sequence identifies
then-approved evidence. Pending/rejected content is restricted to author and staff;
public history retains formerly visible activity with currently restricted sources
redacted. Later approval never makes a submission public retroactively. The chart
marks visible evidence activity with local timestamps and equal sequence-based
spacing, not elapsed-time spacing. Availability proves neither reading nor causation.
Submitted titles/summaries persist; external pages are not archived and may change.

## 7. What fails at one hundred users?

No capacity limit has been measured. One hundred open Room views add about 50 state
queries/second before result reloads. Long histories require repeated replay, Blazor
connections consume server resources, and write bursts contend for SQLite's single
writer.

## 8. What fails at one hundred thousand concurrent users?

For 100,000 attempts in one minute, I expect the global write transaction/SQLite lock
to bottleneck first; connection or memory limits may intervene earlier. Keep
append-only history, deterministic replay and idempotency. Move to PostgreSQL,
per-question sequencing/chains and incremental projections; one popular question
still needs bounded write concurrency. A database swap alone is insufficient.
Acknowledge only durable commits; retain request IDs for retries and transactional
uniqueness/latest-vote checks. Reject overload explicitly with a retryable response,
never false success. Publish batched, versioned updates after commit with reconnect
resynchronization instead of per-viewer polling. Cache aggregates by question,
boundary and algorithm; label delayed results. Caching identity, duplicate decisions
or private evidence without authorization/invalidation risks wrong counts or disclosure.

## 9. What would change before production?

Add staff authentication, abuse controls, monitored backups/restore drills, persistent
cookie-protection keys and tested connection recovery. Replace polling with committed-change
notifications and measure performance. Restrict database privileges and retain external
chain checkpoints; result records also need independent retention. The 22 focused SQLite
tests cover core correctness and role boundaries, not browser behaviour, capacity or a
complete security audit.

## 10. With one more day, what would you redesign first?

The read path: rebuildable projections with explicit sequence/version boundaries,
server-side timeline pagination and a shared Room refresh source. Keep event replay
as the reference calculation and compare projections against it before serving them.

**AI use:** I used an AI coding assistant for planning, implementation, explanations,
documentation and the focused tests. The tests passed; they demonstrate those
scenarios, not independent assurance. No LLM runs inside the product.