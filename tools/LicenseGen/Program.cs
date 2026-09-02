using Spider8DAQ.Core.Licensing;

if (args.Length == 0 || args[0] is "-h" or "--help" or "/?")
{
    Console.WriteLine("""
        UpetLicenseGen — generare chei personale UPET AcqLab (nu catman/HBM)

        Utilizare:
          UpetLicenseGen "<Nume titular>"
          UpetLicenseGen "<Nume titular>" --expires 2027-12-31
          UpetLicenseGen "<Nume titular>" -n 5

        Format cheie: UPET-ACQLAB-XXXX-XXXX-XXXX-XXXX
        """);
    return 0;
}

var name = args[0];
DateTime? expires = null;
var count = 1;

for (var i = 1; i < args.Length; i++)
{
    if (args[i] is "--expires" or "-e" && i + 1 < args.Length)
    {
        if (!DateTime.TryParse(args[++i], out var dt))
        {
            Console.Error.WriteLine("Dată expirare invalidă (folosiți yyyy-MM-dd).");
            return 1;
        }
        expires = dt.Date;
    }
    else if (args[i] is "-n" or "--count" && i + 1 < args.Length)
    {
        count = Math.Clamp(int.Parse(args[++i]), 1, 100);
    }
}

Console.WriteLine($"Produs : {UpetLicense.ProductId}");
Console.WriteLine($"Titular: {name}");
Console.WriteLine($"Expiră : {(expires is null ? "perpetuă" : expires.Value.ToString("yyyy-MM-dd"))}");
Console.WriteLine();

for (var i = 0; i < count; i++)
{
    var key = UpetLicense.GenerateKey(name, expires);
    if (!UpetLicense.TryValidateKey(key, name, out var err))
    {
        Console.Error.WriteLine("Eroare internă validare: " + err);
        return 2;
    }
    Console.WriteLine(key);
}

return 0;
