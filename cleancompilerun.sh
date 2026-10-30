dotnet clean MacDisplay/MacDisplay.csproj
# rm -r MacDisplay/bin/Debug/net10.0-macos/osx-arm64/IFSRenderer.app
dotnet build MacDisplay/MacDisplay.csproj && ./IFSRenderer.app/Contents/MacOS/IFSRenderer
