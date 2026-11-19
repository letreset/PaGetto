using System;
using System.Collections.Generic;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// The malicious package advisories of an ecosystem that changed after a time.
/// </summary>
/// <param name="NewestUtc">The newest change of the ecosystem's data, of any advisory; null when nothing changed.</param>
/// <param name="Ids">The changed malicious package advisories, newest first.</param>
/// <param name="TooMany">Whether more changed than were asked for; <paramref name="Ids"/> is then incomplete.</param>
public record OsvDataChanges(DateTime? NewestUtc, IReadOnlyList<string> Ids, bool TooMany);
