using DocAssistant.Gateway.Dtos.Chat;
using DocAssistant.Gateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Controllers
{
    [Route("api/chat")]
    [ApiController]
    public class ChatController(
        ILogger<ChatController> logger,
        IChatService chatService) : ControllerBase
    {
        //File types allowed to upload
        private readonly string[] _allowedExtensions = { ".pdf" };

        [HttpPost]
        public async Task<IActionResult> CreateChat([FromForm] CreateChatRequest request)
        {

            /* TODO: Implement this scenario:
             * User uploads files
             * uploaded files saved in database with loading status
             * frontend keeps file state as loading
             * RabbitMQ sends files to the AI Service
             * AI Service performs vectorizing and saving files
             * AI Service returns success if it is
                ** AI Service fails (with SignalR ?)
             * Based on return, file status updated
             * Based on last file status, frontend updates state
             * **/

            // 1. Input Validation (Controller's Job)
            if (request.Files == null || request.Files.Count == 0)
            {
                return BadRequest("At least one PDF file is required.");
            }

            foreach (var file in request.Files)
            {
                if (!_allowedExtensions.Contains(Path.GetExtension(file.FileName).ToLower()))
                    return BadRequest($"File '{file.FileName}' is not allowed.");
            }

            try
            {
                // 2. Delegate to Service (The Heavy Lifting)
                //TODO: Fix the file name (it sees id as name)
                var chat = await chatService.CreateChatAsync(request);

                // 3. Return Success
                return Ok(new
                {
                    ChatId = chat.Id,
                    Message = "Chat created successfully. Waiting documents to be uploaded."
                });
            }
            catch (Exception ex)
            {
                // Log the generic error here (Specific errors were logged in the Service)
                logger.LogError(ex, "Error in CreateChat endpoint");

                // Return 500 Internal Server Error
                return StatusCode(500, "An error occurred while creating the chat. Please try again.");
            }
        }

        [HttpDelete]
        public async Task<IActionResult> Delete([FromBody] Guid chatId)
        {
            await chatService.DeleteChatAsync(chatId);

            return NoContent();
        }

    }
}
