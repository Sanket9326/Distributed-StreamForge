#!lua flags=no-writes
-- @common
expect(KEYS[1], 'string'); expect(KEYS[2], 'string')
return {redis.call('GET', KEYS[1]) or false, redis.call('GET', KEYS[2]) or ''}
