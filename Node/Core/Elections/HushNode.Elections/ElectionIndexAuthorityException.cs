namespace HushNode.Elections;

/// <summary>Infrastructure/unsupported-authority fault, never an expected business rejection.
/// Escapes committed indexing so the block checkpoint cannot advance past an unindexed operation.</summary>
public sealed class ElectionIndexAuthorityException(string message, Exception? inner = null) : Exception(message, inner);
