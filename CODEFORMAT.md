## CODEFORMAT.md

Please save these code format rules so that when you generate code, you will always follow them.

ALWAYS make sure to check for changes before editing any files.

Code style (enforced via .editorconfig)
- Indent with 2 spaces.
- Always use braces on control blocks; never single-line bodies!
- Braces on new lines for classes; same line for everything else.
- Use var only when the type is obvious from the right-hand side; otherwise, use explicit types.
- Prefer string interpolation and nameof().
- Prefer pattern matching where it makes sense.
- Prefer null-coalescing and null-conditional operators where it makes sense.
- Use async/await for asynchronous code.
- Use namespaces that match folder structure; use file-scoped namespaces at top of file.
- Usings after namespaces; System.* usings first, then third-party, then project.
- Private fields: _camelCase; const fields: PascalCase.
- All methods: PascalCase; All members: PascalCase.
- Variables inside methods: camelCase.
- When a method has parameters, add a space before and after the parenthesis `method( int i, string x )`. Methods with no parameters do not have a space `method()`

That .editorconfig file already matches the format I specified (and more).
This is just the most important parts.
