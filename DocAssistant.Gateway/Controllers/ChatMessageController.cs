using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DocAssistant.Gateway.Controllers
{
    [Route("api/chat/{chatId:Guid}")]
    [ApiController]
    public class ChatMessageController : ControllerBase
    {
    }
}
