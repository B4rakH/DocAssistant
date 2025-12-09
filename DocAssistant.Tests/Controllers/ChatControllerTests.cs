using DocAssistant.Gateway.Controllers;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;
using DocAssistant.Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace DocAssistant.Tests.Controllers
{
    public class ChatControllerTests
    {
        private readonly Mock<ILogger<ChatController>> _mockLogger;
        private readonly Mock<IChatService> _mockChatService;
        private readonly ChatController _controller;

        public ChatControllerTests()
        {
            _mockLogger = new Mock<ILogger<ChatController>>();
            _mockChatService = new Mock<IChatService>();
            _controller = new ChatController(_mockLogger.Object, _mockChatService.Object);
        }

        #region Helper Methods

        private static IFormFile CreateMockFormFile(string fileName, string content = "test content")
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var stream = new MemoryStream(bytes);
            
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.FileName).Returns(fileName);
            mockFile.Setup(f => f.Length).Returns(bytes.Length);
            mockFile.Setup(f => f.OpenReadStream()).Returns(stream);
            mockFile.Setup(f => f.ContentType).Returns("application/pdf");
            mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns((Stream target, CancellationToken token) =>
                {
                    stream.Position = 0;
                    return stream.CopyToAsync(target, token);
                });

            return mockFile.Object;
        }

        #endregion

        #region Success Tests

        [Fact]
        public async Task CreateChat_WithValidPdfFile_ReturnsOkResult()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile("test.pdf")
                }
            };

            var expectedChat = new Chat
            {
                Id = Guid.NewGuid(),
                Name = "Test Chat"
            };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ReturnsAsync(expectedChat);

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnValue = okResult.Value;
            
            var chatIdProperty = returnValue?.GetType().GetProperty("ChatId");
            var messageProperty = returnValue?.GetType().GetProperty("Message");
            
            Assert.NotNull(chatIdProperty);
            Assert.NotNull(messageProperty);
            Assert.Equal(expectedChat.Id, chatIdProperty.GetValue(returnValue));
        }

        [Fact]
        public async Task CreateChat_WithMultiplePdfFiles_ReturnsOkResult()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Multi File Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile("file1.pdf"),
                    CreateMockFormFile("file2.pdf"),
                    CreateMockFormFile("file3.pdf")
                }
            };

            var expectedChat = new Chat
            {
                Id = Guid.NewGuid(),
                Name = "Multi File Chat"
            };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ReturnsAsync(expectedChat);

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockChatService.Verify(s => s.CreateChatWithDocumentsAsync(request), Times.Once);
        }

        [Fact]
        public async Task CreateChat_CallsChatService_WithCorrectRequest()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile> { CreateMockFormFile("test.pdf") }
            };

            var expectedChat = new Chat { Id = Guid.NewGuid(), Name = "Test Chat" };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.Is<CreateChatRequest>(r => 
                    r.Name == request.Name && r.Files.Count == request.Files.Count)))
                .ReturnsAsync(expectedChat);

            // Act
            await _controller.CreateChat(request);

            // Assert
            _mockChatService.Verify(
                s => s.CreateChatWithDocumentsAsync(It.Is<CreateChatRequest>(r => 
                    r.Name == request.Name && r.Files.Count == 1)),
                Times.Once);
        }

        #endregion

        #region Validation Tests

        [Fact]
        public async Task CreateChat_WithNullFiles_ReturnsBadRequest()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = null!
            };

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("At least one PDF file is required.", badRequestResult.Value);
        }

        [Fact]
        public async Task CreateChat_WithEmptyFilesList_ReturnsBadRequest()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>()
            };

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("At least one PDF file is required.", badRequestResult.Value);
        }

        [Fact]
        public async Task CreateChat_WithNonPdfFile_ReturnsBadRequest()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile("document.docx")
                }
            };

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("File 'document.docx' is not allowed.", badRequestResult.Value);
        }

        [Fact]
        public async Task CreateChat_WithMixedFileTypes_ReturnsBadRequest()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile("valid.pdf"),
                    CreateMockFormFile("invalid.txt")
                }
            };

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("File 'invalid.txt' is not allowed.", badRequestResult.Value);
        }

        [Theory]
        [InlineData("test.txt")]
        [InlineData("test.docx")]
        [InlineData("test.jpg")]
        [InlineData("test.exe")]
        public async Task CreateChat_WithInvalidExtension_ReturnsBadRequest(string fileName)
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile(fileName)
                }
            };

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal($"File '{fileName}' is not allowed.", badRequestResult.Value);
        }

        [Theory]
        [InlineData("test.PDF")]
        [InlineData("test.Pdf")]
        [InlineData("test.PdF")]
        public async Task CreateChat_WithPdfExtensionDifferentCasing_ReturnsOkResult(string fileName)
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile(fileName)
                }
            };

            var expectedChat = new Chat { Id = Guid.NewGuid(), Name = "Test Chat" };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ReturnsAsync(expectedChat);

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task CreateChat_WhenServiceThrowsException_ReturnsInternalServerError()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile> { CreateMockFormFile("test.pdf") }
            };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal("An error occurred while creating the chat. Please try again.", statusCodeResult.Value);
        }

        [Fact]
        public async Task CreateChat_WhenServiceThrowsException_LogsError()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile> { CreateMockFormFile("test.pdf") }
            };

            var expectedException = new Exception("Test exception");

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ThrowsAsync(expectedException);

            // Act
            await _controller.CreateChat(request);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error in CreateChat endpoint")),
                    expectedException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task CreateChat_WhenServiceThrowsDbException_ReturnsInternalServerError()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile> { CreateMockFormFile("test.pdf") }
            };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ThrowsAsync(new InvalidOperationException("Transaction failed"));

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task CreateChat_WithFileNameContainingSpecialCharacters_ReturnsOkResult()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile("file with spaces & special (chars).pdf")
                }
            };

            var expectedChat = new Chat { Id = Guid.NewGuid(), Name = "Test Chat" };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ReturnsAsync(expectedChat);

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task CreateChat_WithEmptyChatName_ReturnsOkResult()
        {
            // Arrange
            var request = new CreateChatRequest
            {
                Name = "",
                Files = new List<IFormFile> { CreateMockFormFile("test.pdf") }
            };

            var expectedChat = new Chat { Id = Guid.NewGuid(), Name = "" };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ReturnsAsync(expectedChat);

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task CreateChat_WithVeryLongFileName_ReturnsOkResult()
        {
            // Arrange
            var longFileName = new string('a', 200) + ".pdf";
            var request = new CreateChatRequest
            {
                Name = "Test Chat",
                Files = new List<IFormFile>
                {
                    CreateMockFormFile(longFileName)
                }
            };

            var expectedChat = new Chat { Id = Guid.NewGuid(), Name = "Test Chat" };

            _mockChatService
                .Setup(s => s.CreateChatWithDocumentsAsync(It.IsAny<CreateChatRequest>()))
                .ReturnsAsync(expectedChat);

            // Act
            var result = await _controller.CreateChat(request);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        #endregion
    }
}
