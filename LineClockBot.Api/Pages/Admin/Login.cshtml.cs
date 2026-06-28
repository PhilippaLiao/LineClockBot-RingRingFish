using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LineClockBot.Api.Pages.Admin;

public class LoginModel(IConfiguration config) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("AdminAuthed") == "true")
            return RedirectToPage("/Admin/Reminders");
        return Page();
    }

    public IActionResult OnPost(string apiKey)
    {
        var expectedKey = config["Internal:ApiKey"];
        if (apiKey == expectedKey)
        {
            HttpContext.Session.SetString("AdminAuthed", "true");
            return RedirectToPage("/Admin/Reminders");
        }

        ErrorMessage = "API Key 不正確，請重試";
        return Page();
    }
}
