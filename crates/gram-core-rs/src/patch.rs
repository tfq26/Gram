#[derive(Debug, Clone)]
pub struct PatchResult {
    pub success: bool,
    pub original_hash: String,
    pub new_hash: String,
    pub diff: String,
}

pub fn create_unified_diff(old_content: &str, new_content: &str, file_name: &str) -> String {
    format!("--- a/{}\n+++ b/{}\n@@ -1 +1 @@\n-{}", file_name, file_name, old_content)
}
