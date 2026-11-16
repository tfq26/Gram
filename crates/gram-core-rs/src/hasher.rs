use std::collections::hash_map::DefaultHasher;
use std::hash::{Hash, Hasher};
use std::fs;
use std::path::Path;

pub fn hash_bytes(data: &[u8]) -> String {
    let mut hasher = DefaultHasher::new();
    data.hash(&mut hasher);
    format!("{:016x}", hasher.finish())
}

pub fn hash_file<P: AsRef<Path>>(path: P) -> std::io::Result<String> {
    let content = fs::read(path)?;
    Ok(hash_bytes(&content))
}
