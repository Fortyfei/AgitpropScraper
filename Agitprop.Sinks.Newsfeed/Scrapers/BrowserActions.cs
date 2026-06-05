using Agitprop.Core.Interfaces;

using PuppeteerSharp;

namespace Agitprop.Sinks.Newsfeed.Scrapers;

/// <summary>
/// Represents a browser action for scrolling through the Negynegynegy archive pages.
/// </summary>
internal class NegynegynegyArchiveScrollAction : IBrowserAction
{
    /// <summary>
    /// Executes the scrolling action on the specified browser page.
    /// </summary>
    /// <param name="page">The browser page to perform the action on.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ExecuteAsync(IPage page)
    {
        //Accept GDPR cookies if present
        var cookieAcceptSelector = "#accept-btn";
        await page.WaitForSelectorAsync(cookieAcceptSelector);
        await page.ClickAsync(cookieAcceptSelector);
        bool hasNext = true;
        do
        {
            try
            {
                // /html/body/div[1]/div/div[4]/div[3]/button
                var loadBtnSelector = "#body > div:nth-child(8) > div > div._2r9i95._1chu0ywh.p4kpu3e.p4kpu3hp.p4kpu3i4 > div._1chu0ywh.p4kpu3fk._1chu0yw9._2r9i9e.slotDoubleColumn > button";
                var btn = await page.QuerySelectorAsync(loadBtnSelector);
                await btn.ClickAsync();
                // Click the button
                await page.WaitForNetworkIdleAsync();
            }
            catch (Exception)
            {
                hasNext = false;
            }
        } while (hasNext);
    }
}
