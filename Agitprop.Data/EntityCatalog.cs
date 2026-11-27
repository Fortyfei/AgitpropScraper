namespace Agitprop.Data;

public sealed record CatalogAlias(string Alias, int Priority = 0);

public sealed record CatalogEntity(
    Guid Id,
    string Name,
    string Type,
    IReadOnlyList<CatalogAlias> Aliases);

/// <summary>
/// Curated entities whose identity is decided editorially: canonical name, fixed NER type and the
/// inflected/press forms seen in Hungarian news. Entities are seeded into the database, so the
/// runtime resolver only has to consult the alias table. Validation runs on first access and fails
/// fast on a cross-type or ambiguous alias, mirroring the source catalog's duplicate-host check.
/// </summary>
public static class EntityCatalog
{
    public static IReadOnlyList<CatalogEntity> All { get; } = Create();

    public static void Validate(IEnumerable<CatalogEntity> entities)
    {
        var list = entities.ToList();

        var duplicateId = list.GroupBy(entity => entity.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException($"Catalog entity id '{duplicateId.Key}' is used more than once.");
        }

        var duplicateName = list
            .GroupBy(entity => (Name: EntityNormalizer.Normalize(entity.Name), entity.Type))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidOperationException(
                $"Catalog contains more than one '{duplicateName.Key.Type}' entity named '{duplicateName.Key.Name}'.");
        }

        var aliases = list
            .SelectMany(entity => entity.Aliases
                .Append(new CatalogAlias(entity.Name))
                .Select(alias => (entity.Id, entity.Name, entity.Type, Alias: EntityNormalizer.Normalize(alias.Alias), alias.Priority)))
            .ToList();

        foreach (var group in aliases.GroupBy(alias => (alias.Alias, alias.Type)))
        {
            if (string.IsNullOrEmpty(group.Key.Alias))
            {
                throw new InvalidOperationException("Catalog contains an empty alias.");
            }

            var distinctEntities = group.DistinctBy(alias => alias.Id).ToList();
            if (distinctEntities.Count <= 1)
            {
                continue;
            }

            var priorities = distinctEntities.Select(alias => alias.Priority).ToList();
            if (priorities.Distinct().Count() != priorities.Count)
            {
                throw new InvalidOperationException(
                    $"Alias '{group.Key.Alias}' ({group.Key.Type}) points to several entities without distinct priorities: " +
                    string.Join(", ", distinctEntities.Select(alias => alias.Name)));
            }
        }
    }

    private static IReadOnlyList<CatalogEntity> Create()
    {
        var entities = new List<CatalogEntity>
        {
            new(Guid.Parse("7ab2e33d-d585-54f9-a137-d54845129290"), "Magyarország", "LOC",
            [
                new("Magyarországon"), new("Magyarországra"), new("Magyarországról"), new("Magyarországnak"),
                new("Magyarországot"), new("Magyarországgal"), new("Magyarországból"), new("Magyarországé"),
                new("Magyarországért")
            ]),
            new(Guid.Parse("289575a6-51cb-528a-8fd4-d126d426374c"), "Ukrajna", "LOC",
            [
                new("Ukrajnában"), new("Ukrajnába"), new("Ukrajnából"), new("Ukrajnának"), new("Ukrajnát"),
                new("Ukrajnán"), new("Ukrajnától"), new("Ukrajnával"), new("Ukrajnáért")
            ]),
            new(Guid.Parse("7f713d4c-21ae-5424-aa10-fbad1f7688eb"), "Oroszország", "LOC",
            [
                new("Oroszországban"), new("Oroszországra"), new("Oroszországról"), new("Oroszországnak"),
                new("Oroszországot"), new("Oroszországgal"), new("Oroszországból")
            ]),
            new(Guid.Parse("3622df15-0d92-5f58-a8e0-adfd62cb1663"), "Szlovákia", "LOC",
            [
                new("Szlovákiában"), new("Szlovákiába"), new("Szlovákiából"), new("Szlovákiának"),
                new("Szlovákiát"), new("Szlovákiával")
            ]),
            new(Guid.Parse("2c4d06f6-80cc-5d5b-a8be-6ee99a7d282c"), "Európa", "LOC",
            [
                new("Európában"), new("Európába"), new("Európából"), new("Európának"), new("Európát"),
                new("Európáért"), new("Európával")
            ]),
            new(Guid.Parse("b757e44b-0836-5e25-b901-f0a8519b4576"), "Amerika", "LOC",
            [
                new("Amerikában"), new("Amerikába"), new("Amerikából"), new("Amerikának"), new("Amerikát")
            ]),
            new(Guid.Parse("06c7ceef-5710-5414-96eb-66e917197480"), "Kárpátalja", "LOC",
            [
                new("Kárpátalján"), new("Kárpátaljára"), new("Kárpátaljáról"), new("Kárpátaljának"),
                new("Kárpátalját")
            ]),
            new(Guid.Parse("911574d6-856c-5641-911f-25ec9a8ccfd5"), "Budapest", "LOC",
            [
                new("Budapesten"), new("Budapestre"), new("Budapestről"), new("Budapestnek"),
                new("Budapesttel"), new("Budapestet")
            ]),
            new(Guid.Parse("a452a454-5535-5577-9ad8-8930e5863387"), "Kijev", "LOC",
            [
                new("Kijevben"), new("Kijevbe"), new("Kijevből"), new("Kijevnek"), new("Kijevet")
            ]),
            new(Guid.Parse("9bebe3ea-e0f1-5b22-9eed-463dc880d7a3"), "München", "LOC",
            [
                new("Münchenben"), new("Münchenbe"), new("Münchenből"), new("Münchennek")
            ]),
            new(Guid.Parse("c4906ecd-059d-5f60-9cb7-9f765e347340"), "Brüsszel", "LOC",
            [
                new("Brüsszelben"), new("Brüsszelbe"), new("Brüsszelből"), new("Brüsszelnek"),
                new("Brüsszelt")
            ]),
            new(Guid.Parse("b95bca20-f0f4-5c52-a489-cab1c63d08f8"), "Strasbourg", "LOC",
            [
                new("Strasbourgban"), new("Strasbourgba")
            ]),
            new(Guid.Parse("d4289988-cfda-5dfe-8b0f-25dbd8268fca"), "Tisza", "LOC",
            [
                new("Tiszán"), new("Tiszára"), new("Tiszáról"), new("Tiszában")
            ]),
            new(Guid.Parse("a75621db-d835-5fe1-9d17-cfcee4257e7d"), "Vlagyimir Putyin", "PER",
            [
                new("Putyin"), new("Putyinra"), new("Putyinról"), new("Putyinnak"), new("Putyint"),
                new("Putyinnal"), new("Putyinért")
            ]),
            new(Guid.Parse("99498fb3-edec-573b-843e-4e551119a10c"), "Donald Trump", "PER",
            [
                new("Trump"), new("Trumpra"), new("Trumpnak"), new("Trumpot"), new("Trumppal")
            ]),
            new(Guid.Parse("58c2d1b0-e1d2-570b-9f69-681bd97acb66"), "Joe Biden", "PER",
            [
                new("Biden"), new("Bidenre"), new("Bidennek"), new("Bident"), new("Bidennel")
            ]),
            new(Guid.Parse("68bc2ef2-972c-596e-bdbd-f5d5e0119790"), "Orbán Viktor", "PER",
            [
                new("Orbán"), new("Orbánra"), new("Orbánnak"), new("Orbánt"), new("Orbánnal")
            ]),
            new(Guid.Parse("b49b1e27-3604-5507-8524-917f764e04fd"), "Magyar Péter", "PER",
            [
                new("Magyar Péternek"), new("Magyar Péterre"), new("Magyar Pétert"),
                new("Magyar Péterrel"), new("Magyar Péterről")
            ]),
            new(Guid.Parse("77d6bf7b-a42f-55cf-96c1-2e1aaa80bf9c"), "Volodimir Zelenszkij", "PER",
            [
                new("Zelenszkij"), new("Zelenszkijnek"), new("Zelenszkijt"), new("Zelenszkijjel"),
                new("Zelenszkijre")
            ]),
            new(Guid.Parse("8b39a7c2-6a44-5d96-9b9a-86cb5274c58b"), "Robert Fico", "PER",
            [
                new("Fico"), new("Ficonak")
            ]),
            new(Guid.Parse("77fb503e-e18d-5f33-ae1c-5ec871479810"), "Matolcsy György", "PER",
            [
                new("Matolcsy"), new("Matolcsynak"), new("Matolcsyt"), new("Matolcsyval")
            ]),
            new(Guid.Parse("a931b44d-e82d-5207-99de-8ebccaffbccd"), "Európai Unió", "ORG",
            [
                new("EU"), new("Európai Uniónak"), new("Európai Unióban"), new("Európai Unióból"),
                new("Európai Uniót"), new("Európai Unióval"), new("Európai Unióét"),
                new("EU-nak"), new("EU-ban"), new("EU-t")
            ]),
            new(Guid.Parse("46e1adeb-42b8-5683-bbba-b3f49c49176b"), "Európai Bizottság", "ORG",
            [
                new("Európai Bizottságnak"), new("Európai Bizottságot"), new("Európai Bizottsághoz"),
                new("Európai Bizottságban")
            ]),
            new(Guid.Parse("1aa5245b-63ab-50c1-9580-9b4ed7db9a3c"), "Európai Parlament", "ORG",
            [
                new("Európai Parlamentnek"), new("Európai Parlamentet"), new("Európai Parlamentben"),
                new("Európai Parlamenthez")
            ]),
            new(Guid.Parse("e78bc067-65c0-53a2-bce3-319aeb5fb1d4"), "Európai Néppárt", "ORG",
            [
                new("Európai Néppártnak"), new("Európai Néppártot")
            ]),
            new(Guid.Parse("d6ef58f8-2acd-5b21-9cdb-a5a5fb9c137d"), "ENSZ", "ORG",
            [
                new("ENSZ-nek"), new("ENSZ-t"), new("ENSZ-ben"), new("ENSZ-nél")
            ]),
            new(Guid.Parse("339cd24d-a481-5784-9997-2de6c3beb667"), "NATO", "ORG",
            [
                new("NATO-nak"), new("NATO-t"), new("NATO-ban"), new("NATO-ba")
            ]),
            new(Guid.Parse("11a4ac27-98bd-5236-955c-36370e517758"), "MNB", "ORG",
            [
                new("Magyar Nemzeti Bank"), new("MNB-t"), new("MNB-nek"), new("MNB-ben")
            ]),
            new(Guid.Parse("03aafafa-06fe-55e9-ba42-3a6a0f687833"), "Fidesz", "ORG",
            [
                new("Fidesznek"), new("Fideszt"), new("Fidesszel"), new("Fideszre")
            ]),
            new(Guid.Parse("33d1933c-068c-59f0-826a-aee3ab62a912"), "Tisza Párt", "ORG",
            [
                new("Tisza"), new("Tiszának"), new("Tisza Pártnak"), new("Tisza Pártot")
            ]),
            new(Guid.Parse("2cf47b89-c4dd-57e5-8045-b88df8ec3055"), "DK", "ORG",
            [
                new("Demokratikus Koalíció"), new("DK-nak"), new("DK-t"), new("DK-ban")
            ]),
            new(Guid.Parse("421c938b-71f6-5b54-8a83-9cb7b24b8cec"), "Momentum", "ORG",
            [
                new("Momentum Mozgalom"), new("Momentumnak"), new("Momentumot")
            ]),
            new(Guid.Parse("78158dc8-457e-5de3-80d6-998b1f7a5513"), "Podmaniczky Mozgalom", "ORG",
            [
                new("Podmaniczky Mozgalomnak"), new("Podmaniczky Mozgalmat")
            ]),
            new(Guid.Parse("9a9cd00e-f24a-579f-a3d1-51b68b14d45b"), "Századvég", "ORG",
            [
                new("Századvégnek"), new("Századvéget")
            ])
        };

        Validate(entities);
        return entities;
    }
}
