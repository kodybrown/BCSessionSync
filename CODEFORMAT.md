# Code Style and Formatting

Please save these code format rules so that when you generate code, you will always follow them.

ALWAYS make sure to check for changes before editing any files.

Code style (enforced via .editorconfig)
- Indent with 2 spaces.
- Private fields: _camelCase; const fields: KEBAB_CASE.
- All methods: PascalCase; All members: PascalCase.
- Variables inside methods: camelCase.
- When a method has parameters, add a space before and after the parenthesis `method( int i, string x )`.
- Methods with no parameters do not have a space `method()`
- Always use braces on control blocks; never single-line bodies!
- Braces on new lines for classes; same line for everything else.

C# coding conventions to follow:
- Prefer var over explicit types.
- Prefer string interpolation and nameof().
- Prefer pattern matching where it makes sense.
- Prefer null-coalescing and null-conditional operators where it makes sense.
- Use async/await for asynchronous code.
- Use namespaces that match folder structure; use file-scoped namespaces at top of file.
- Usings after namespaces; System.* usings first, then third-party, then project.

That '.editorconfig' and 'omnisharp.json' files match the format I have specified here (and more).
This is just the most important parts.
