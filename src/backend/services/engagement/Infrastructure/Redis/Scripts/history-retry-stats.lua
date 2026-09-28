#!lua flags=no-writes
-- @common
expect(KEYS[1], 'zset'); expect(KEYS[2], 'zset')
local top = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
return {redis.call('ZCARD', KEYS[1]), redis.call('ZCARD', KEYS[2]), #top > 0 and top[2] or false}
