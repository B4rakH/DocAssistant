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

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var chats = await chatService.GetAllAsync();
            return Ok(chats);
        }


        [HttpGet("{chatId:Guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid chatId)
        {
            var chat = await chatService.GetByIdAsync(chatId);

            return chat == null ? NotFound("Chat cannot found") : Ok(chat);
        }

        [HttpPost]
        public async Task<IActionResult> CreateChat([FromForm] string chatName)
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

            try
            {
                var chat = await chatService.CreateChatAsync(chatName);

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

        [HttpPost("{chatId:Guid}/files")]
        public async Task<IActionResult> AddFiles([FromBody] List<IFormFile> files,
            [FromRoute] Guid chatId)
        {
            // 1. Input Validation (Controller's Job)
            if (files == null || files.Count == 0)
            {
                return BadRequest("At least one PDF file is required.");
            }

            foreach (var file in files)
            {
                if (!_allowedExtensions.Contains(Path.GetExtension(file.FileName).ToLower()))
                    return BadRequest($"File '{file.FileName}' is not allowed.");
            }

            try
            {
                await chatService.UploadFilesAsync(chatId, files);
                return Accepted();
            }
            catch (Exception ex)
            {
                // Log the generic error here (Specific errors were logged in the Service)
                logger.LogError(ex, "Error in UploadFile endpoint");
                // Return 500 Internal Server Error
                return StatusCode(500, "An error occurred while creating the chat. Please try again.");
            }
        }

        [HttpPost("{chatId:Guid}/message")]
        public async Task<IActionResult> PostMessage([FromBody] string message, [FromRoute] Guid chatId)
        {
            // TODO: Implement message posting logic

            return Ok();
        }

        [HttpDelete]
        public async Task<IActionResult> Delete([FromBody] Guid chatId)
        {
            await chatService.DeleteChatAsync(chatId);

            return NoContent();
        }

    }
}
