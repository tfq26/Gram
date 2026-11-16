/**
 * Maps a model identifier or provider name to its static vendor logo path.
 */
export function getModelLogoPath(modelId: string, provider?: string): string {
  const key = (modelId || '').toLowerCase();
  const prov = (provider || '').toLowerCase();
  if (key.includes('anthropic') || key.includes('claude')) return '/logo/anthropic.svg';
  if (key.includes('openai') || key.includes('gpt-')) return '/logo/openai-light.svg';
  if (key.includes('deepseek')) return '/logo/Deepseek-logo-icon.svg.webp';
  if (key.includes('google') || key.includes('gemini')) return '/logo/gemini.svg';
  if (key.includes('meta') || key.includes('llama') || key.includes('muse')) return '/logo/meta-12361.svg';
  if (key.includes('x-ai') || key.includes('xai') || key.includes('grok')) return '/logo/XAI_Logo.svg.webp';
  if (
    key !== 'auto' &&
    (prov.includes('bedrock') ||
      key.includes('amazon') ||
      key.includes('nova') ||
      key.includes('titan') ||
      key.includes('bedrock') ||
      key.startsWith('us.') ||
      key.startsWith('eu.') ||
      key.startsWith('ap.'))
  ) {
    return '/logo/bedrock.png';
  }
  return '/logo/openrouter-color.svg';
}
