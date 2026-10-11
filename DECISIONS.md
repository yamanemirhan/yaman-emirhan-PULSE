# Decisions

## Duplicate votes and revisions

A participant has one effective vote per question: their latest recorded answer
and confidence. Earlier revisions remain in the event history.

A submission matching both fields of that participant's latest vote is a no-op:
it creates no event. Changing either the answer or confidence creates a revision.
Returning to an earlier answer after an intervening change is also a revision.

The UI disables updates when the loaded vote is unchanged. The server independently
checks the latest vote inside the same SQLite write transaction as the append,
so concurrent identical submissions cannot both create a revision.

Identity comes from a protected browser cookie, not a verified person. Refreshing
the page keeps the identity, but clearing cookies or using another browser or
device can create another participant. This discourages straightforward repeat
voting; it does not guarantee one human, one vote.

## Roles and the local demo

All three roles can create questions, vote, revise their own vote and read current
results. Only Administrator can run chain verification or inspect technical
stored-result replays; this is enforced in the services as well as the UI. No role
may overwrite or delete recorded history through the application.
