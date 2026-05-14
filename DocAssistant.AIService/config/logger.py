"""
Logging configuration for the application.
Combines console output with rotating file logs.
"""

import logging
from logging.handlers import RotatingFileHandler
import os


def setup_logging():
    """
    Configure logging for the entire application.
    - Console handler for stdout
    - Rotating file handler (10 MB per file, 5 backups)
    """
    # Create logs directory
    os.makedirs('logs', exist_ok=True)

    # Setup handlers
    file_handler = RotatingFileHandler(
        'logs/consumer.log', maxBytes=10 * 1024 * 1024, backupCount=5
    )
    console_handler = logging.StreamHandler()

    # Configure root logger
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        datefmt='%Y-%m-%d %H:%M:%S',
        handlers=[file_handler, console_handler]
    )
