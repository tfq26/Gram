import { describe, it, expect } from 'bun:test';
import { AHPServer } from '../src/sync/ahpProtocol';
import { GramConnectManager } from '../src/sync/connectPresence';

describe('AHP v2 & Gram Connect Presence Sync', () => {
  it('handles sequence-delta synchronization via sinceSeq', () => {
    const ahp = new AHPServer();
    const session = ahp.createSession({ workspacePath: '/app' });

    ahp.appendMessage(session.sessionId, { role: 'user', content: 'Turn 1' });
    ahp.appendMessage(session.sessionId, { role: 'assistant', content: 'Turn 2' });
    ahp.appendMessage(session.sessionId, { role: 'user', content: 'Turn 3' });

    // Client requests only messages sinceSeq 2
    const delta = ahp.syncSessionDelta(session.sessionId, 2);
    expect(delta.deltaMessages.length).toBe(1);
    expect(delta.deltaMessages[0]!.content).toBe('Turn 3');
    expect(delta.latestSeq).toBe(3);
  });

  it('records device presence and casts active session across devices', () => {
    const manager = new GramConnectManager();

    manager.recordHeartbeat({
      deviceId: 'macbook-pro',
      deviceName: 'Taufeeq MacBook',
      deviceType: 'cli',
      isExecutionHost: true,
    });

    manager.recordHeartbeat({
      deviceId: 'phone-mobile',
      deviceName: 'iPhone Companion',
      deviceType: 'mobile',
    });

    const online = manager.listOnlineDevices();
    expect(online.length).toBe(2);

    const cast = manager.castSession('phone-mobile', 'macbook-pro', 'sess-123');
    expect(cast.success).toBe(true);
    expect(cast.event?.targetDeviceId).toBe('macbook-pro');
    expect(cast.event?.sessionId).toBe('sess-123');
  });
});
