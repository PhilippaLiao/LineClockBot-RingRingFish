using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LineClockBot.Api.Pages.Admin;

public abstract class AdminPageBase : PageModel
{
    protected IActionResult? CheckAuth()
    {
        if (HttpContext.Session.GetString("AdminAuthed") != "true")
            return RedirectToPage("/Admin/Login");
        return null;
    }
}
