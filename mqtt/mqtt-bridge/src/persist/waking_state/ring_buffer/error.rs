#[derive(Debug, thiserror::Error)]
pub enum BlockError {
    #[error("Unexpected block crc {found:?} expected {expected:?}")]
    BlockCrc { found: u32, expected: u32 },

    #[error("Failed to create block. Caused by {0}")]
    BlockCreation(#[from] bincode::Error),

    #[error("Unexpected data crc {found:?} expected {expected:?}")]
    DataCrc { found: u32, expected: u32 },

    #[error("Unexpected data size {found:?} expected {expected:?}")]
    DataSize { found: u64, expected: u64 },

    #[error("Bad hint")]
    Hint,
}

#[derive(Debug, thiserror::Error)]
pub enum RingBufferError {
    #[error("Underlying block error occurred. Caused by {0}")]
    Block(BlockError),

    #[error("Flushing failed. Caused by {0}")]
    Flush(std::io::Error),

    #[error("Buffer is full and messages must be drained to continue")]
    Full,

    #[error("Unable to create file. Caused by {0}")]
    FileCreate(std::io::Error),

    #[error("File IO error occurred. Caused by {0}")]
    FileIO(std::io::Error),

    #[error("Storage file metadata unavailable. Caused by {0}")]
    FileMetadata(std::io::Error),

    #[error(
        "Storage file cannot be truncated. Caused by new max size {new} being less than {current}"
    )]
    FileTruncation { current: u64, new: u64 },

    #[error("Key does not exist")]
    NonExistantKey,

    #[error("Key is at invalid index for removal")]
    RemovalIndex,

    #[error("Cannot remove before reading")]
    RemoveBeforeRead,

    #[error("Serialization error occurred. Caused by {0}")]
    Serialization(#[from] bincode::Error),

    #[error("Failed to validate internal details. Caused by {0}")]
    Validate(BlockError),
}
