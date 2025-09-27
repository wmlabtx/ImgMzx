# Copilot Repository Instructions

Context:
- Default language: C#
- Target: .NET 8

Style:
- Write comments in English only.
- Create new files in UTF-8 coding only.
- Keep comments minimal; add only when non-obvious.
- Prefer concise answers and small, focused code snippets.
- Use modern C# features compatible with .NET 8.
- Avoid using unicode characters in comments and messages.

Conventions:
- Prefer Span<T>/Memory<T> and SIMD when appropriate.
- Prefer stackalloc for small local arrays.
- Avoid unnecessary allocations.
- Favor clear naming and early exits.
- Unit tests: MSTest (Assert.*).

When uncertain, ask concise clarifying questions first.