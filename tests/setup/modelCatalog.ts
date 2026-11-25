/**
 * Bun test preload: load a deterministic snapshot of Lyra's model catalog so routing tests do not
 * depend on the network, a disk cache, or the 4-model built-in fallback.
 * Regenerate the fixture from Lyra's `listGatewayLlmModels('standard')` when the catalog changes.
 */
import fixture from '../fixtures/lyra-model-catalog.json';
import { setModelCatalog, type LyraCatalogResponse } from '../../src/providers/modelCatalog';

setModelCatalog(fixture as LyraCatalogResponse, 'test');
