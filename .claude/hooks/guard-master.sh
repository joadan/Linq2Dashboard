#!/usr/bin/env bash
# PreToolUse hook for the Bash and PowerShell tools: refuse a git commit or
# git push while master is checked out. Every change lands through a pull
# request (CLAUDE.md, "Git workflow"). Exit 2 blocks the call and returns the
# message to the assistant; anything else lets the call through.
input=$(cat)
# Only a git command that starts a shell statement counts, so the words in a
# heredoc, a commit message or a comment do not trigger the guard.
start='(^|"command":"|\n|[;&|(][[:space:]]*)'
pattern="${start}git[[:space:]]+(-C[[:space:]]+[^[:space:]]+[[:space:]]+)?(commit|push)([[:space:]]|\\n|\"|$)"
if ! printf '%s' "$input" | grep -qE "$pattern"; then
  exit 0
fi
# The branch is read where the command runs, not where the hook runs: the hook
# starts in the main checkout, while a session in a worktree runs its commands
# in the input's "cwd". A "git -C <dir>" is taken relative to that. Paths never
# hold a quote, so a JSON string is read up to the next one and its escaped
# backslashes are then halved.
unescape() { sed -e 's/\\\\/\\/g'; }
dir=$(printf '%s' "$input" | grep -oE '"cwd"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n 1 | sed -E 's/^"cwd"[[:space:]]*:[[:space:]]*"(.*)"$/\1/' | unescape)
other=$(printf '%s' "$input" | grep -oE "$pattern" | head -n 1 | sed -nE 's/.*git[[:space:]]+-C[[:space:]]+([^[:space:]]+).*/\1/p' | sed -e 's/\\"//g' | unescape)
if [ -n "$other" ]; then
  case "$other" in
    /* | [A-Za-z]:*) dir=$other ;;
    *) dir=${dir:-.}/$other ;;
  esac
fi
branch=$(git -C "${dir:-.}" symbolic-ref --short -q HEAD 2>/dev/null)
if [ "$branch" = "master" ]; then
  echo "Blocked: master is checked out. Create a branch (git switch -c <name>), commit there, push it and open a pull request with 'gh pr create'." >&2
  exit 2
fi
exit 0
