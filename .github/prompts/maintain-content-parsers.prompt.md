name: Maintain Content Parsers
version: 1.0.0
description: Maintain and update content parsers for news sites by running tests, updating test data, and verifying parser correctness
tags:
  - content-parsers
  - testing
  - maintenance
  - newsfeed

## Task: Maintain Content Parsers

This task provides a systematic approach to maintain and update content parsers for news sites in the AgitpropScraper project.

### Task Steps

1. **Run Content Parser Tests**: Execute both online and offline tests to identify failures
2. **Analyze Failures**: Review test output to identify which sites are failing and which expected texts map to multiple HTML snapshots
3. **Update Test Data**: Fetch current HTML for failed sites and save dated snapshots into the site testdata directory
4. **Fix Parsers**: Update content parsers based on the newest HTML snapshot while keeping older selectors compatible
5. **Verify Changes**: Run offline tests, then online tests to verify correctness across all snapshots
6. **Commit Changes**: Use git to commit test data and parser updates

### Quick Start Commands

```bash
# Run online tests to identify failures
dotnet test --filter "FullyQualifiedName~ContentParserOnlineTests"

# Run offline tests to verify parser changes
dotnet test --filter "FullyQualifiedName~ContentParserOfflineTests"
```

### Task Invocation

To start this task, run:
```bash
dotnet test --filter "FullyQualifiedName~ContentParserOnlineTests"
```

This will execute the online content parser tests and identify which news sites are failing.

## Steps

### Step 1: Run Online Tests
First, run the online content parser tests to identify which sites are failing:

```bash
dotnet test --filter "FullyQualifiedName~ContentParserOnlineTests"
```
First run `dotnet build` and confirm it exits with code 0. If the build fails, fix compilation errors before interpreting any test output. Only proceed to parse test results after a clean build.

### Step 2: Identify Failed Sites
Review the test output to identify which news sites are failing. Note the site names and any error messages.

### Step 3: Fetch Current HTML for Failed Sites
For each failed site and each affected article snapshot:

1. Navigate to the site in a browser
2. Copy the full HTML content
3. Save it to the appropriate testdata directory:
   - Location: `Agitprop.Sinks.Newsfeed_Test/TestData/{siteName}/{articleDate}.html`
   - Use the article's publish date as the filename so UI changes can be tracked over time
   - Keep older dated snapshots when they represent a distinct historical UI variant for the same expected text
4. Update the testcase entry so the same expected text can point to multiple HTML files when needed

### Step 4: Update Content Parsers
Based on the new test data, update the corresponding content parser:

1. Open the content parser file for the site
2. Review the parsing logic
3. Update the parser to correctly extract content from the new HTML structure
4. Prefer adding or reordering XPath fallbacks instead of replacing the older selector outright, so the parser stays backward compatible
5. Test the updated parser with every HTML snapshot for that expected text

### Step 5: Run Offline Tests
Run the offline content parser tests to verify your updates:

First run `dotnet build` and confirm it exits with code 0. If the build fails, fix compilation errors before interpreting any test output. Only proceed to parse test results after a clean build.
```bash
dotnet test --filter "FullyQualifiedName~ContentParserOfflineTests"
```

### Step 6: Run Online Tests Again
Run the online tests again to verify that all parsers are working correctly:

First run `dotnet build` and confirm it exits with code 0. If the build fails, fix compilation errors before interpreting any test output. Only proceed to parse test results after a clean build.
```bash
dotnet test --filter "FullyQualifiedName~ContentParserOnlineTests"
```

If Step 6 reveals a site that was passing before Step 1 but is now failing, stop and do not commit. Run `git diff` to identify which parser change introduced the regression, revert that specific file with `git checkout -- <file>`, and re-run offline and online tests before proceeding.

If online tests still fail after two full iterations of Steps 3–6 for the same site, stop iterating for that site and add a comment in the parser file: // TODO: Parser broken as of {date} — site structure may require manual inspection. Commit the working sites separately and log the failing site name for human follow-up.

### Step 7: Commit Changes
Use git to commit your changes at meaningful steps:

1. After updating test data: `git add Agitprop.Sinks.Newsfeed_Test/TestData/{siteName}/`
2. After successfully updating a parser: `git add Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers/{SiteName}ArticleContentParser.cs`
3. After all changes are complete: 
   - If updating a single site: `git commit -m "Update content parser for {siteName}: refresh HTML fixture and parser logic"`
   - If updating multiple sites in one pass: `git commit -m "Refresh HTML fixtures and parsers for: {site1}, {site2}, ..."`

## Definitions

In all commands below, {siteName} is the lowercase directory name as it appears under Agitprop.Sinks.Newsfeed_Test/TestData/ (e.g., bbc-news). {SiteName} is the PascalCase class name prefix as it appears in the ContentParsers directory (e.g., BbcNews). Derive both values from the failing test name shown in Step 2 output. The test name will appear as ContentParserOnlineTests.{SiteName}Test. Strip the trailing "Test" suffix to get {SiteName} (PascalCase). Convert to kebab-case to get {siteName} (e.g., BbcNews → bbc-news).

When a testcase needs more than one HTML snapshot for the same expected text, keep the snapshots under the same site folder and distinguish them by article publish date in the filename. The testcase may point to multiple `HtmlPath` values for the same expected content.

## Example Invocation

To use this prompt, you would typically:

1. Start with running the online tests
2. Follow the steps above for any failed sites
3. Repeat until all tests pass
4. Commit your changes

## Tips

- Always run offline tests before committing to ensure your parser changes work correctly
- Use git diff to review changes before committing
- Keep a record of which sites you've updated
- Test one site at a time to isolate issues
- If a parser needs significant changes, consider creating a backup first
- When updating a parser, preserve older XPath fallbacks unless they are known to be obsolete

## Troubleshooting

If tests continue to fail:

1. Check that the HTML files are correctly saved
2. Verify that the testcases.json files are properly formatted
3. Review the parser logic against the new HTML structure
4. Run tests with more verbose output to see specific failures
5. Consider using browser developer tools to inspect the current site structure
6. If a VS Code task fails with `Path to shell executable ... does not exist`, ensure process tasks use split command/args (for example: `"command": "dotnet"` with `"args": ["test", "--filter", "FullyQualifiedName~ContentParserOnlineTests"]`) or run the equivalent command directly in the terminal.