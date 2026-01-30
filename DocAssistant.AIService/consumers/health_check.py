"""
Health Check HTTP Server
Provides a simple endpoint to monitor service health
"""

from http.server import HTTPServer, BaseHTTPRequestHandler
import json
import threading
import logging

logger = logging.getLogger(__name__)


class HealthCheckHandler(BaseHTTPRequestHandler):
    """HTTP request handler for health checks"""
    
    # Class variable to store the consumer instance
    consumer = None
    
    def do_GET(self):
        """Handle GET requests"""
        if self.path == '/health':
            self.handle_health_check()
        elif self.path == '/':
            self.handle_root()
        else:
            self.send_error(404, "Not Found")
    
    def handle_root(self):
        """Handle root path - show available endpoints"""
        response = {
            "service": "DocAssistant AI Service",
            "endpoints": {
                "/health": "Health check endpoint",
                "/": "This help message"
            }
        }
        self.send_json_response(response, 200)
    
    def handle_health_check(self):
        """Handle /health endpoint"""
        health_status = {
            "service": "DocAssistant AI Service",
            "status": "healthy",
            "checks": {}
        }
        
        overall_healthy = True
        
        # Check 1: RabbitMQ connection
        try:
            if self.consumer and self.consumer.connection and self.consumer.connection.is_open:
                health_status["checks"]["rabbitmq"] = "connected"
            else:
                health_status["checks"]["rabbitmq"] = "disconnected"
                overall_healthy = False
        except Exception as e:
            health_status["checks"]["rabbitmq"] = f"error: {str(e)}"
            overall_healthy = False
        
        # Check 2: AI Service (embedding model)
        try:
            if self.consumer and self.consumer.ai_service and self.consumer.ai_service.embedding_model:
                model_name = self.consumer.ai_service.embedding_model_name
                health_status["checks"]["ai_model"] = f"loaded: {model_name}"
            else:
                health_status["checks"]["ai_model"] = "not loaded"
                overall_healthy = False
        except Exception as e:
            health_status["checks"]["ai_model"] = f"error: {str(e)}"
            overall_healthy = False
        
        # Check 3: Qdrant connection
        try:
            if self.consumer and self.consumer.qdrant_service:
                if self.consumer.qdrant_service.health_check():
                    health_status["checks"]["qdrant"] = "connected"
                else:
                    health_status["checks"]["qdrant"] = "disconnected"
                    overall_healthy = False
            else:
                health_status["checks"]["qdrant"] = "not initialized"
                overall_healthy = False
        except Exception as e:
            health_status["checks"]["qdrant"] = f"error: {str(e)}"
            overall_healthy = False
        
        # Check 4: MinIO connection
        try:
            if self.consumer and self.consumer.minio_service:
                if self.consumer.minio_service.health_check():
                    health_status["checks"]["minio"] = "connected"
                else:
                    health_status["checks"]["minio"] = "disconnected"
                    overall_healthy = False
            else:
                health_status["checks"]["minio"] = "not initialized"
                overall_healthy = False
        except Exception as e:
            health_status["checks"]["minio"] = f"error: {str(e)}"
            overall_healthy = False
        
        # Set overall status
        if not overall_healthy:
            health_status["status"] = "unhealthy"
        
        # Return appropriate HTTP status code
        status_code = 200 if overall_healthy else 503
        self.send_json_response(health_status, status_code)
    
    def send_json_response(self, data, status_code=200):
        """Send JSON response"""
        self.send_response(status_code)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps(data, indent=2).encode())
    
    def log_message(self, format, *args):
        """Override to use our logger instead of printing to stderr"""
        logger.info("%s - %s" % (self.address_string(), format % args))


class HealthCheckServer:
    """HTTP server for health checks running in background thread"""
    
    def __init__(self, consumer, host='0.0.0.0', port=8080):
        """
        Initialize health check server
        
        Args:
            consumer: DocumentConsumer instance to check
            host: Host to bind to (0.0.0.0 = all interfaces)
            port: Port to listen on
        """
        self.host = host
        self.port = port
        self.server = None
        self.thread = None
        
        # Set the consumer instance for the handler
        HealthCheckHandler.consumer = consumer
    
    def start(self):
        """Start the health check server in a background thread"""
        try:
            self.server = HTTPServer((self.host, self.port), HealthCheckHandler)
            
            # Run server in background thread
            self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
            self.thread.start()
            
            logger.info(f"Health check server started at http://{self.host}:{self.port}/health")
            
        except Exception as e:
            logger.error(f"Failed to start health check server: {e}")
    
    def stop(self):
        """Stop the health check server"""
        if self.server:
            logger.info("Stopping health check server...")
            self.server.shutdown()
            self.server.server_close()
            if self.thread:
                self.thread.join(timeout=5)
            logger.info("Health check server stopped")
