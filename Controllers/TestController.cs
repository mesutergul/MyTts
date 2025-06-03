// In Controllers/TestController.cs
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic; // Required for KeyNotFoundException

namespace MyTts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        [HttpGet("throw-server-error")]
        public IActionResult ThrowServerError()
        {
            throw new Exception("This is a test server error!");
        }

        [HttpGet("throw-not-found")]
        public IActionResult ThrowNotFound()
        {
            throw new KeyNotFoundException("This is a test KeyNotFoundException, simulating a resource not found.");
        }

        [HttpGet("throw-app-exception")]
        public IActionResult ThrowApplicationException()
        {
            throw new ApplicationException("This is a test ApplicationException.");
        }
    }
}
