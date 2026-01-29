import logging
from logging.handlers import RotatingFileHandler
import os

# Create logs directory
os.makedirs('logs', exist_ok=True)

# Setup handlers
file_handler = RotatingFileHandler('logs/consumer.log', maxBytes=10*1024*1024, backupCount=5)
console_handler = logging.StreamHandler()

# Configure logging
logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[file_handler, console_handler]
)
