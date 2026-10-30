# AI Usage

How AI tools were used on this project, what was checked by hand, and where the AI's output was changed or rejected.

## Tools

| Tool | Used for |
| --- | --- |
| Claude Code | Implementation, in small reviewed steps |
| TODO | Design documents (HLD, LLD) |

## How the work was run

`CLAUDE.md` holds the project rules: stack, conventions, domain rules that must never be broken, and the working agreement. Claude Code works one commit-sized step at a time, builds and runs tests, then stops. The author reviews each step and commits it.

## Step log

| Step | What the AI did | What was reviewed or changed |
| --- | --- | --- |
| 1. Repo setup | Extended `.gitignore` and `.gitattributes`, wrote `.env.example` and skeleton docs | TODO |

## Where the AI was wrong or needed correcting

TODO

## Deviations from the design

Recorded in [DECISIONS.md](DECISIONS.md#deviations-from-the-lld).
