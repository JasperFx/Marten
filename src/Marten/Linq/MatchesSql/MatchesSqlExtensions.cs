#nullable enable
using System;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.MatchesSql;

public static class MatchesSqlExtensions
{
    /// <summary>
    ///     The search results should match the specified where fragment.
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="whereFragment"></param>
    /// <returns></returns>
    public static bool MatchesSql(this object doc, ISqlFragment whereFragment)
    {
        throw new NotSupportedException(
            $"{nameof(MatchesSql)} extension method can only be used in Marten Linq queries.");
    }

    /// <summary>
    ///     The search results should match the specified raw sql fragment. "?" is assumed to be a place holder
    /// for parameters. Use the MatchesJsonPath() usage instead if your SQL utilizes any JSONPath operators!
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static bool MatchesSql(this object doc, string sql, params object[] parameters)
    {
        throw new NotSupportedException(
            $"{nameof(MatchesSql)} extension method can only be used in Marten Linq queries.");
    }

    /// <summary>
    ///     The search results should match the specified raw sql fragment.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static bool MatchesSql(this object doc, char placeholder, string sql, params object[] parameters)
    {
        throw new NotSupportedException(
            $"{nameof(MatchesSql)} extension method can only be used in Marten Linq queries.");
    }

    /// <summary>
    ///     The search results should match the specified raw sql fragment that is assumed to include JSONPath. Use "^" for parameters instead of "?" to disambiguate from JSONPath
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static bool MatchesJsonPath(this object doc, string sql, params object[] parameters)
    {
        throw new NotSupportedException(
            $"{nameof(MatchesJsonPath)} extension method can only be used in Marten Linq queries.");
    }
}
