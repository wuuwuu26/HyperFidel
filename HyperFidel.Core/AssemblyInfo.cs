using System.Runtime.CompilerServices;

// Grant the HyperFidel Windows desktop app access to internal Core types.
// The server project (HyperFidel.Server) intentionally does NOT have access
// to audio-processing internals — it serves files via HTTP, not sample data.
[assembly: InternalsVisibleTo("HyperFidel")]
[assembly: InternalsVisibleTo("HyperFidel.Core.Tests")]
