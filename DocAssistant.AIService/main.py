import logging
from consumers.massTransit import DocumentConsumer


def setup_logging():
    """Configure logging for the application"""
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        datefmt='%Y-%m-%d %H:%M:%S'
    )


if __name__ == '__main__':
    setup_logging()
    consumer = DocumentConsumer()
    consumer.start_consuming()