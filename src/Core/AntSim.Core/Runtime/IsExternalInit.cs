// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Shim para `record` en netstandard2.1 (F5.2c): los records de
    /// <c>NodeGene</c>/<c>ConnGene</c> requieren este tipo, que el BCL de
    /// netstandard2.1 no define. Internal — no forma parte de la superficie
    /// pública del Core.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
