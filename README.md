# Cyber Gemini

Cyber Gemini is a portable Windows utility for discovering duplicate files by size and cryptographic hash. It also highlights files that share names (regardless of size or hash) and provides quick renaming tools.

## Highlights
- Multi-core scanning using size-first grouping and SHA-256 hashing
- Modern, muted UI tuned for long sessions
- Actions for move, delete to recycle bin, or permanent delete with warning
- Optional backup of selected files before deletion

## Build
Open the solution in Visual Studio 2022 (or later) and run the `CyberGemini` project.

## Publish a single-file EXE
```bash
# From the CyberGemini project folder
 dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true
```
The portable executable will be in:
```
CyberGemini\bin\Release\net8.0-windows\win-x64\publish
```