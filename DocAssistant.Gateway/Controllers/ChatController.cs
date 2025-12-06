using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;
using DocAssistant.Gateway.Repositories;
using DocAssistant.Gateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Controllers
{
    [Route("api/chat")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;

        private readonly IChatService _chatService;

        //File types allowed to upload
        private readonly string[] _allowedExtensions = { ".pdf" };

        public ChatController(
            ILogger<ChatController> logger,
            IChatService chatService)
        {
            _logger = logger;
            _chatService = chatService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateChat([FromForm] CreateChatRequest request)
        {

            /* TODO: Implement this scenario:
             * User uploads files
             * uploaded files saved in database with loading status
             * frontend keeps file state as loading
             * RabbitMQ sends files to the AI Service
             * AI Service performs vectorizing and saving files
             * AI Service returns success (or fail)
             * Based on return, file status updated
             * Based on last file status, frontend updates state
             * **/

            // 1. Input Validation (Controller's Job)
            if (request.Files == null || request.Files.Count == 0)
            {
                return BadRequest("At least one PDF file is required.");
            }

            // Optional: You can keep simple file extension checks here 
            // or move them to the Service. Usually, basic validation stays in Controller.
            foreach (var file in request.Files)
            {
                if (!_allowedExtensions.Contains(Path.GetExtension(file.FileName).ToLower()))
                    return BadRequest($"File '{file.FileName}' is not allowed.");
            }

            try
            {
                // 2. Delegate to Service (The Heavy Lifting)
                var chat = await _chatService.CreateChatWithDocumentsAsync(request);

                // 3. Return Success
                return Ok(new
                {
                    ChatId = chat.Id,
                    Message = "Chat created successfully. Processing started."
                });
            }
            catch (Exception ex)
            {
                // Log the generic error here (Specific errors were logged in the Service)
                _logger.LogError(ex, "Error in CreateChat endpoint");

                // Return 500 Internal Server Error
                return StatusCode(500, "An error occurred while creating the chat. Please try again.");
            }
        }
    }
}
