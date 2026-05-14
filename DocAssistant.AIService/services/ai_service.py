"""
AI Service for Document Processing
Handles PDF extraction, text chunking, and embedding generation
"""

import os
from typing import List, Dict
import pdfplumber
from PyPDF2 import PdfReader
from sentence_transformers import SentenceTransformer
import logging

logger = logging.getLogger(__name__)


class AIService:
    """
    Main AI service for processing documents
    """
    
    def __init__(self, embedding_model_name: str):
        """
        Initialize the AI service
        
        Args:
            embedding_model_name: Name of the embedding model to use
        """
        logger.info("Initializing AI Service...")
        
        # Chunking configuration
        self.chunk_size = 1000  # Characters per chunk
        self.chunk_overlap = 200  # Overlap between chunks
        
        # Load embedding model
        self.embedding_model_name = embedding_model_name
        self.embedding_model = None
        self._load_embedding_model()
        
        logger.info("AI Service initialized")
    
    def _load_embedding_model(self):
        """
        Load the embedding model into memory
        
        This model converts text into numerical vectors (embeddings)
        that can be stored and searched in a vector database.
        """
        try:
            logger.info(f"Loading embedding model: {self.embedding_model_name}")
            
            # Load the model
            self.embedding_model = SentenceTransformer(self.embedding_model_name)
            
            # Get model info
            embedding_dim = self.embedding_model.get_sentence_embedding_dimension()
            logger.info(f"Model loaded successfully")
            logger.info(f"Embedding dimension: {embedding_dim}")
            
        except Exception as e:
            logger.error(f"Failed to load embedding model: {e}")
            raise
        
    
    def extract_text_from_pdf(self, file_path: str) -> str:
        """
        Extract text from PDF file
        
        Args:
            file_path: Path to PDF file
            
        Returns:
            Extracted text as string
            
        Raises:
            FileNotFoundError: If file doesn't exist
            Exception: If extraction fails
        """
        
        # Step 1: Verify file exists
        if not os.path.exists(file_path):
            raise FileNotFoundError(f"PDF file not found: {file_path}")
        
        logger.info(f"Extracting text from: {os.path.basename(file_path)}")
        
        text = ""
        
        # Step 2: Try pdfplumber first (better for complex PDFs)
        try:
            text = self._extract_with_pdfplumber(file_path)
            if text.strip():
                logger.info(f"Extracted {len(text)} characters using pdfplumber")
                return text
        except Exception as e:
            logger.warning(f"pdfplumber failed: {e}")
        
        # Step 3: Fallback to PyPDF2
        try:
            text = self._extract_with_pypdf2(file_path)
            if text.strip():
                logger.info(f"Extracted {len(text)} characters using PyPDF2")
                return text
        except Exception as e:
            logger.error(f"PyPDF2 also failed: {e}")
            raise Exception("Failed to extract text from PDF")
        
        if not text.strip():
            raise Exception("PDF appears to be empty or scanned (no text found)")
        
        return text
    
    def _extract_with_pdfplumber(self, file_path: str) -> str:
        """Extract text using pdfplumber (better quality)"""
        text = ""
        with pdfplumber.open(file_path) as pdf:
            for page_num, page in enumerate(pdf.pages, 1):
                page_text = page.extract_text()
                if page_text:
                    text += page_text + "\n\n"
        return text
    
    def _extract_with_pypdf2(self, file_path: str) -> str:
        """Extract text using PyPDF2 (fallback method)"""
        text = ""
        reader = PdfReader(file_path)
        for page_num, page in enumerate(reader.pages, 1):
            page_text = page.extract_text()
            if page_text:
                text += page_text + "\n\n"
        return text
    
    def chunk_text(self, text: str) -> List[Dict[str, any]]:
        """
        Split text into smaller chunks for processing
        
        Why chunking?
        - AI models have token limits
        - Smaller chunks = better search results
        - Overlap ensures context isn't lost between chunks
        
        Args:
            text: Full document text
            
        Returns:
            List of chunks with metadata
        """
        logger.info(f"Chunking text ({len(text)} characters)...")
        
        # Clean the text first
        text = text.strip()
        
        if not text:
            return []
        
        chunks = []
        start = 0
        chunk_index = 0
        
        # Loop through text, creating overlapping chunks
        while start < len(text):
            # Calculate end position
            end = start + self.chunk_size
            
            # Get the chunk
            chunk_text = text[start:end]
            
            # Only add if chunk has meaningful content
            if chunk_text.strip():
                chunks.append({
                    'index': chunk_index,
                    'text': chunk_text,
                    'start_pos': start,
                    'end_pos': end,
                    'length': len(chunk_text)
                })
                chunk_index += 1
            
            # Move start position (with overlap)
            start += (self.chunk_size - self.chunk_overlap)
        
        logger.info(f"Created {len(chunks)} chunks")
        return chunks
    
    def generate_embeddings(self, chunks: List[Dict[str, any]]) -> List[Dict[str, any]]:
        """
        Generate vector embeddings for text chunks
        
        Converts text into numerical vectors that represent meaning.
        Similar texts will have similar vectors.
        
        Args:
            chunks: List of text chunks with metadata
            
        Returns:
            Same chunks with added 'embedding' field
        """
        
        if not chunks:
            logger.warning("No chunks to embed")
            return []
        
        logger.info(f"Generating embeddings for {len(chunks)} chunks...")
        
        # Extract just the text from chunks
        texts = [chunk['text'] for chunk in chunks]
        
        # Generate embeddings in batch (faster than one-by-one)
        embeddings = self.embedding_model.encode(
            texts,
            show_progress_bar=True,
            batch_size=32  # Process 32 chunks at a time
        )
        
        # Add embeddings back to chunks
        for i, chunk in enumerate(chunks):
            chunk['embedding'] = embeddings[i].tolist()  # Convert numpy array to list
            chunk['embedding_dim'] = len(embeddings[i])
        
        logger.info(f"Embeddings generated (dimension: {len(embeddings[0])})")
        return chunks

