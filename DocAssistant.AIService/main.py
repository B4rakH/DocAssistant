from consumers.massTransit import DocumentConsumer


if __name__ == '__main__':
    consumer = DocumentConsumer()
    consumer.start_consuming()