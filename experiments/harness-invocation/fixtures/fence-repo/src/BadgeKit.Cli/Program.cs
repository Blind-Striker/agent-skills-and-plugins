using System.Globalization;
using BadgeKit;
using Microsoft.Extensions.Logging.Abstractions;

// Usage: BadgeKit.Cli [<key> <owner> <label>]. Without arguments it prints a demo badge.
var request = args.Length == 3
    ? new BadgeRequest(args[0], args[1], args[2], [])
    : new BadgeRequest("istanbul-42", "ilker", "istanbul office", ["demo"]);

var issuer = new BadgeIssuer(NullLogger<BadgeIssuer>.Instance);
if (!issuer.TryIssue(request, out var label))
{
    Console.Error.WriteLine($"invalid key: {request.Id}");
    return 1;
}

// Badges print in Turkish upper case, so "istanbul" becomes "İSTANBUL".
var turkish = CultureInfo.GetCultureInfo("tr-TR");
Console.WriteLine($"BADGE {label.ToUpper(turkish)}");
return 0;
