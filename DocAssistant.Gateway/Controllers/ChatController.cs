using DocAssistant.Gateway.Dtos.Chat;
using DocAssistant.Gateway.Dtos.ChatMessage;
using DocAssistant.Gateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Controllers
{
    [ApiController]
    [Route("api/chats")]
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
        public async Task<IActionResult> CreateChat([FromBody] CreateChatRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var chat = await chatService.CreateChatAsync(request.Name);

                return Ok(new
                {
                    ChatId = chat.Id,
                    Message = "Chat created successfully. You can now upload documents."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in CreateChat endpoint");
                return StatusCode(500, "An error occurred while creating the chat. Please try again.");
            }
        }

        [HttpPost("{chatId:Guid}/files")]
        public async Task<IActionResult> UploadFiles([FromForm] List<IFormFile> files,
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
        public async Task<IActionResult> PostMessage(
            [FromBody] ChatMessageRequest request,
            [FromRoute] Guid chatId)
        {
            // TODO: Implement message posting logic
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await chatService.PostMessageAsync(chatId, request);

            return Ok(response);
        }

    }
}
