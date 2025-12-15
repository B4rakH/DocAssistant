using DocAssistant.Gateway.Repositories;
using DocAssistant.Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DocAssistant.Gateway.Controllers
{
    [Route("api/chat/{chatId:Guid}/messages")]
    [ApiController]
    public class ChatMessageController(IChatMessageService messageService) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAll(Guid chatId)
        {
            var messages = await messageService.GetAllMesageAsync(chatId);
            return Ok(messages);
        }

        [HttpPost]
        public async Task<IActionResult> AddFiles([FromBody] List<IFormFile> files)
        {
            //TODO: Implement adding new files to the chat (CHECK BEFORE ADDITION)
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> PostMessage()
        {
            //TODO: Implement sending message to AI service and saving response
            return Ok();
        }
    }
}
