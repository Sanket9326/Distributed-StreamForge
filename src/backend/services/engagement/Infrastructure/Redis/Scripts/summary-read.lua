#!lua flags=no-writes
-- @common
expect(KEYS[1], 'set'); expect(KEYS[2], 'set'); expect(KEYS[3], 'string'); expect(KEYS[4], 'string')
return {redis.call('SCARD', KEYS[1]), redis.call('SCARD', KEYS[2]),
  redis.call('GET', KEYS[3]) or false, redis.call('GET', KEYS[4]) or false}
