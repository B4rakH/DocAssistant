from config.logger import setup_logging
from config.settings import Settings
from consumers.document_consumer import DocumentConsumer


if __name__ == '__main__':
    setup_logging()
    settings = Settings()
    consumer = DocumentConsumer(settings)
    consumer.start_consuming()