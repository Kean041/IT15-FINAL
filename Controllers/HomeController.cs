using Microsoft.AspNetCore.Mvc;

namespace FinSight.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error(int? statusCode)
        {
            ViewBag.StatusCode = statusCode ?? 500;
            ViewBag.Message = statusCode switch
            {
                404 => "The page you're looking for doesn't exist.",
                403 => "You don't have permission to access this resource.",
                _ => "Something went wrong. Please try again later."
            };
            return View();
        }
    }
}
