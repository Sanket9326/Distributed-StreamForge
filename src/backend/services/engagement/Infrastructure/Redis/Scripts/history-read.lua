#!lua flags=no-writes
-- @common
expect(KEYS[1], 'hash'); expect(KEYS[2], 'string')
return redis.call('HGET', KEYS[1], ARGV[1]) or redis.call('GET', KEYS[2])
